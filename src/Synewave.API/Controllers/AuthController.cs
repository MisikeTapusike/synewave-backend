using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.DTOs;
using Synewave.API.Models;
using Synewave.API.Services;
using System.Security.Claims;

namespace Synewave.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuthService _auth;
    private readonly ISpotifyService _spotify;

    public AuthController(AppDbContext db, IAuthService auth, ISpotifyService spotify)
    {
        _db = db;
        _auth = auth;
        _spotify = spotify;
    }

    /// <summary>Register a new user</summary>
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new ApiResponse<AuthResponse>(false, null, "Email je už obsadený."));

        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new ApiResponse<AuthResponse>(false, null, "Používateľské meno je obsadené."));

        var user = new User
        {
            Username = req.Username.Trim(),
            Email = req.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _auth.GenerateToken(user);
        return Ok(new ApiResponse<AuthResponse>(true, new AuthResponse(token, MapUser(user))));
    }

    /// <summary>Login with email + password</summary>
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.ToLower());

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new ApiResponse<AuthResponse>(false, null, "Nesprávny email alebo heslo."));

        user.LastActiveAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _auth.GenerateToken(user);
        return Ok(new ApiResponse<AuthResponse>(true, new AuthResponse(token, MapUser(user))));
    }

    // ── SPOTIFY ───────────────────────────────────────────────

   [HttpGet("spotify/login")]
public IActionResult SpotifyLogin()
{
    var clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID");
    var redirectUri = Uri.EscapeDataString(Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? "");
    var scopes = Uri.EscapeDataString("user-read-private user-read-email user-top-read user-read-currently-playing user-read-playback-state");
    var state = Guid.NewGuid().ToString("N");
    var url = $"https://accounts.spotify.com/authorize?response_type=code&client_id={clientId}&scope={scopes}&redirect_uri={redirectUri}&state={state}";
    return Redirect(url);
}
    }

    /// <summary>Spotify OAuth callback</summary>
    [HttpGet("spotify/callback")]
    public async Task<IActionResult> SpotifyCallback([FromQuery] string code, [FromQuery] string state)
    {
        // Exchange code for tokens
        var tokens = await _spotify.ExchangeCodeForTokenAsync(code);
        if (tokens == null)
            return BadRequest("Spotify autorizácia zlyhala.");

        // Get Spotify user profile
        var spotifyUser = await _spotify.GetUserProfileAsync(tokens.Access_Token);
        if (spotifyUser == null)
            return BadRequest("Nepodarilo sa načítať Spotify profil.");

        // Find or create user
        var user = await _db.Users.FirstOrDefaultAsync(u => u.SpotifyId == spotifyUser.Id);

        if (user == null)
        {
            // Check if email already exists
            user = await _db.Users.FirstOrDefaultAsync(u => u.Email == spotifyUser.Email.ToLower());

            if (user != null)
            {
                // Link Spotify to existing account
                user.SpotifyId = spotifyUser.Id;
                user.SpotifyAccessToken = tokens.Access_Token;
                user.SpotifyRefreshToken = tokens.Refresh_Token;
                user.SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.Expires_In);
                if (user.AvatarUrl == null && spotifyUser.Images.Any())
                    user.AvatarUrl = spotifyUser.Images.First().Url;
            }
            else
            {
                // Create new user from Spotify
                var username = await GenerateUniqueUsername(spotifyUser.Display_Name);
                user = new User
                {
                    Username = username,
                    Email = spotifyUser.Email.ToLower(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    SpotifyId = spotifyUser.Id,
                    SpotifyAccessToken = tokens.Access_Token,
                    SpotifyRefreshToken = tokens.Refresh_Token,
                    SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.Expires_In),
                    AvatarUrl = spotifyUser.Images.FirstOrDefault()?.Url
                };
                _db.Users.Add(user);
            }
        }
        else
        {
            // Update tokens
            user.SpotifyAccessToken = tokens.Access_Token;
            if (tokens.Refresh_Token != null)
                user.SpotifyRefreshToken = tokens.Refresh_Token;
            user.SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.Expires_In);
            user.LastActiveAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        var jwtToken = _auth.GenerateToken(user);

        // Redirect to frontend with token
        var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:3000";
        return Redirect($"{frontendUrl}?token={jwtToken}");
    }

    /// <summary>Get current user's top Spotify tracks</summary>
    [HttpGet("spotify/top-tracks")]
    [Authorize]
    public async Task<IActionResult> GetTopTracks([FromQuery] string timeRange = "medium_term", [FromQuery] int limit = 20)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(userId);

        if (user?.SpotifyAccessToken == null)
            return BadRequest(new { error = "Spotify účet nie je prepojený." });

        // Refresh token if expired
        if (user.SpotifyTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5) && user.SpotifyRefreshToken != null)
        {
            var refreshed = await _spotify.RefreshTokenAsync(user.SpotifyRefreshToken);
            if (refreshed != null)
            {
                user.SpotifyAccessToken = refreshed.Access_Token;
                user.SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(refreshed.Expires_In);
                await _db.SaveChangesAsync();
            }
        }

        var tracks = await _spotify.GetTopTracksAsync(user.SpotifyAccessToken, timeRange, limit);
        return Ok(new { success = true, data = tracks });
    }

    /// <summary>Get what the current user is listening to right now</summary>
    [HttpGet("spotify/now-playing")]
    [Authorize]
    public async Task<IActionResult> GetNowPlaying()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(userId);

        if (user?.SpotifyAccessToken == null)
            return Ok(new { success = true, data = (object?)null, message = "Spotify nie je prepojený." });

        // Refresh token if expired
        if (user.SpotifyTokenExpiresAt <= DateTime.UtcNow.AddMinutes(5) && user.SpotifyRefreshToken != null)
        {
            var refreshed = await _spotify.RefreshTokenAsync(user.SpotifyRefreshToken);
            if (refreshed != null)
            {
                user.SpotifyAccessToken = refreshed.Access_Token;
                user.SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(refreshed.Expires_In);
                await _db.SaveChangesAsync();
            }
        }

        var nowPlaying = await _spotify.GetCurrentlyPlayingAsync(user.SpotifyAccessToken);
        return Ok(new { success = true, data = nowPlaying });
    }

    // ── HELPERS ───────────────────────────────────────────────

    private async Task<string> GenerateUniqueUsername(string displayName)
    {
        var base_name = new string(displayName
            .ToLower()
            .Replace(" ", "_")
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .Take(20)
            .ToArray());

        if (string.IsNullOrEmpty(base_name)) base_name = "user";

        var username = base_name;
        var counter = 1;

        while (await _db.Users.AnyAsync(u => u.Username == username))
        {
            username = $"{base_name}_{counter++}";
        }

        return username;
    }

    private static UserDto MapUser(User u) =>
        new(u.Id, u.Username, u.Email, u.AvatarUrl, u.LastActiveAt);
}
