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

    public AuthController(AppDbContext db, IAuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new ApiResponse<AuthResponse>(false, null, "Email je uz obsadeny."));

        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new ApiResponse<AuthResponse>(false, null, "Pouzivatelske meno je obsadene."));

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

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.ToLower());

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new ApiResponse<AuthResponse>(false, null, "Nespravny email alebo heslo."));

        user.LastActiveAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _auth.GenerateToken(user);
        return Ok(new ApiResponse<AuthResponse>(true, new AuthResponse(token, MapUser(user))));
    }

    [HttpGet("spotify/login")]
    public IActionResult SpotifyLogin()
    {
        var clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "";
        var redirectUri = Uri.EscapeDataString(Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? "");
        var scopes = Uri.EscapeDataString("user-read-private user-read-email user-top-read user-read-currently-playing user-read-playback-state");
        var state = Guid.NewGuid().ToString("N");
        var url = $"https://accounts.spotify.com/authorize?response_type=code&client_id={clientId}&scope={scopes}&redirect_uri={redirectUri}&state={state}";
        return Redirect(url);
    }

    [HttpGet("spotify/callback")]
    public async Task<IActionResult> SpotifyCallback([FromQuery] string code, [FromQuery] string? state, [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
            return BadRequest($"Spotify error: {error}");

        var clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "";
        var clientSecret = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET") ?? "";
        var redirectUri = Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? "";

        using var http = new System.Net.Http.HttpClient();
        var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        var tokenRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://accounts.spotify.com/api/token");
        tokenRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        tokenRequest.Content = new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        });

        var tokenResponse = await http.SendAsync(tokenRequest);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync();

        if (!tokenResponse.IsSuccessStatusCode)
            return BadRequest(new { error = "token_failed", details = tokenBody });

        var tokens = System.Text.Json.JsonSerializer.Deserialize<SpotifyTokenResult>(tokenBody, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (tokens == null) return BadRequest("Could not parse tokens.");

        var profileRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.spotify.com/v1/me");
        profileRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.Access_Token);
        var profileResponse = await http.SendAsync(profileRequest);
        var profileBody = await profileResponse.Content.ReadAsStringAsync();

        if (!profileResponse.IsSuccessStatusCode)
            return BadRequest(new { error = "profile_failed", details = profileBody });

        var spotifyUser = System.Text.Json.JsonSerializer.Deserialize<SpotifyProfileResult>(profileBody, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (spotifyUser == null) return BadRequest("Could not parse profile.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.SpotifyId == spotifyUser.Id);
        if (user == null)
        {
            user = await _db.Users.FirstOrDefaultAsync(u => u.Email == spotifyUser.Email.ToLower());
            if (user != null)
            {
                user.SpotifyId = spotifyUser.Id;
                user.SpotifyAccessToken = tokens.Access_Token;
                user.SpotifyRefreshToken = tokens.Refresh_Token;
                user.SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.Expires_In);
            }
            else
            {
                var username = (spotifyUser.Display_Name ?? "user").ToLower().Replace(" ", "_");
                if (string.IsNullOrWhiteSpace(username)) username = "user";
                if (await _db.Users.AnyAsync(u => u.Username == username))
                    username = $"{username}_{Guid.NewGuid().ToString("N")[..4]}";

                user = new User
                {
                    Username = username,
                    Email = spotifyUser.Email.ToLower(),
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                    SpotifyId = spotifyUser.Id,
                    SpotifyAccessToken = tokens.Access_Token,
                    SpotifyRefreshToken = tokens.Refresh_Token,
                    SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.Expires_In),
                    AvatarUrl = spotifyUser.Images?.FirstOrDefault()?.Url
                };
                _db.Users.Add(user);
            }
        }
        else
        {
            user.SpotifyAccessToken = tokens.Access_Token;
            if (tokens.Refresh_Token != null) user.SpotifyRefreshToken = tokens.Refresh_Token;
            user.SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.Expires_In);
            user.LastActiveAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        var jwtToken = _auth.GenerateToken(user);
        var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:3000";
        return Redirect($"{frontendUrl}?token={jwtToken}");
    }

    [HttpGet("spotify/top-tracks")]
    [Authorize]
    public async Task<IActionResult> GetTopTracks([FromQuery] string timeRange = "medium_term", [FromQuery] int limit = 20)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(userId);
        if (user?.SpotifyAccessToken == null)
            return BadRequest(new { error = "Spotify ucet nie je prepojeny." });

        using var http = new System.Net.Http.HttpClient();
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,
            $"https://api.spotify.com/v1/me/top/tracks?time_range={timeRange}&limit={limit}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.SpotifyAccessToken);
        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return Ok(System.Text.Json.JsonSerializer.Deserialize<object>(body));
    }

    [HttpGet("spotify/now-playing")]
    [Authorize]
    public async Task<IActionResult> GetNowPlaying()
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(userId);
        if (user?.SpotifyAccessToken == null)
            return Ok(new { success = true, data = (object?)null });

        using var http = new System.Net.Http.HttpClient();
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,
            "https://api.spotify.com/v1/me/player/currently-playing");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.SpotifyAccessToken);
        var response = await http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return Ok(new { success = true, data = (object?)null });
        var body = await response.Content.ReadAsStringAsync();
        return Ok(System.Text.Json.JsonSerializer.Deserialize<object>(body));
    }

    private static UserDto MapUser(User u) =>
        new(u.Id, u.Username, u.Email, u.AvatarUrl, u.LastActiveAt);
}

public class SpotifyTokenResult
{
    public string Access_Token { get; set; } = string.Empty;
    public int Expires_In { get; set; }
    public string? Refresh_Token { get; set; }
}

public class SpotifyProfileResult
{
    public string Id { get; set; } = string.Empty;
    public string? Display_Name { get; set; }
    public string Email { get; set; } = string.Empty;
    public List<SpotifyImageResult>? Images { get; set; }
}

public class SpotifyImageResult
{
    public string Url { get; set; } = string.Empty;
}
