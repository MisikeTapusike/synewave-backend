using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.Models;
using Synewave.API.Services;

namespace Synewave.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpotifyController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAuthService _auth;
    private readonly ISpotifyService _spotify;

    public SpotifyController(AppDbContext db, IAuthService auth, ISpotifyService spotify)
    {
        _db = db;
        _auth = auth;
        _spotify = spotify;
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        var clientId = Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID") ?? "missing";
        var redirectUri = Uri.EscapeDataString(Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? "");
        var scopes = Uri.EscapeDataString("user-read-private user-read-email user-top-read user-read-currently-playing user-read-playback-state");
        var state = Guid.NewGuid().ToString("N");
        var url = $"https://accounts.spotify.com/authorize?response_type=code&client_id={clientId}&scope={scopes}&redirect_uri={redirectUri}&state={state}";
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
    {
        var tokens = await _spotify.ExchangeCodeForTokenAsync(code);
        if (tokens == null)
            return BadRequest("Token exchange failed.");

        var spotifyUser = await _spotify.GetUserProfileAsync(tokens.Access_Token);
        if (spotifyUser == null)
            return BadRequest("Could not get Spotify profile.");

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
                if (user.AvatarUrl == null && spotifyUser.Images.Any())
                    user.AvatarUrl = spotifyUser.Images.First().Url;
            }
            else
            {
                var username = spotifyUser.Display_Name.ToLower().Replace(" ", "_");
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
                    AvatarUrl = spotifyUser.Images.FirstOrDefault()?.Url
                };
                _db.Users.Add(user);
            }
        }
        else
        {
            user.SpotifyAccessToken = tokens.Access_Token;
            if (tokens.Refresh_Token != null)
                user.SpotifyRefreshToken = tokens.Refresh_Token;
            user.SpotifyTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.Expires_In);
            user.LastActiveAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        var jwtToken = _auth.GenerateToken(user);
        var frontendUrl = Environment.GetEnvironmentVariable("FRONTEND_URL") ?? "http://localhost:3000";
        return Redirect($"{frontendUrl}?token={jwtToken}");
    }
}
