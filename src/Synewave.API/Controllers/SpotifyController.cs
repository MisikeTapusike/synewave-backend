using Microsoft.AspNetCore.Mvc;

namespace Synewave.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpotifyController : ControllerBase
{
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
}
