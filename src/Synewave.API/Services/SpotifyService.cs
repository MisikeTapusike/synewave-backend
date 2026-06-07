using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Synewave.API.Models;

namespace Synewave.API.Services;

public interface ISpotifyService
{
    string GetAuthorizationUrl(string state);
    Task<SpotifyTokenResponse?> ExchangeCodeForTokenAsync(string code);
    Task<SpotifyUserProfile?> GetUserProfileAsync(string accessToken);
    Task<List<SpotifyTrack>> GetTopTracksAsync(string accessToken, string timeRange = "medium_term", int limit = 20);
    Task<SpotifyCurrentlyPlaying?> GetCurrentlyPlayingAsync(string accessToken);
    Task<SpotifyTokenResponse?> RefreshTokenAsync(string refreshToken);
}

public class SpotifyService : ISpotifyService
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public SpotifyService(IConfiguration config, HttpClient http)
    {
        _config = config;
        _http = http;
    }

    private string GetClientId() =>
        _config["Spotify:ClientId"] ?? Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_ID")!;

    private string GetClientSecret() =>
        _config["Spotify:ClientSecret"] ?? Environment.GetEnvironmentVariable("SPOTIFY_CLIENT_SECRET")!;

    private string Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? "" =>
        _config["Spotify:RedirectUri"] ?? Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI")!;

    public string GetAuthorizationUrl(string state)
    {
        var clientId = GetClientId();
        var redirectUri = Uri.EscapeDataString(Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? "");
        var scopes = Uri.EscapeDataString("user-read-private user-read-email user-top-read user-read-currently-playing user-read-playback-state");

        return $"https://accounts.spotify.com/authorize?response_type=code&client_id={clientId}&scope={scopes}&redirect_uri={redirectUri}&state={state}";
    }

    public async Task<SpotifyTokenResponse?> ExchangeCodeForTokenAsync(string code)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{GetClientId()}:{GetClientSecret()}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = Environment.GetEnvironmentVariable("SPOTIFY_REDIRECT_URI") ?? ""
        });

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyTokenResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<SpotifyTokenResponse?> RefreshTokenAsync(string refreshToken)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{GetClientId()}:{GetClientSecret()}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyTokenResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<SpotifyUserProfile?> GetUserProfileAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyUserProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<List<SpotifyTrack>> GetTopTracksAsync(string accessToken, string timeRange = "medium_term", int limit = 20)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/me/top/tracks?time_range={timeRange}&limit={limit}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return new List<SpotifyTrack>();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SpotifyTopTracksResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result?.Items ?? new List<SpotifyTrack>();
    }

    public async Task<SpotifyCurrentlyPlaying?> GetCurrentlyPlayingAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.spotify.com/v1/me/player/currently-playing");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _http.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SpotifyCurrentlyPlaying>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}

// ── DTOs ──────────────────────────────────────────────────────

public class SpotifyTokenResponse
{
    public string Access_Token { get; set; } = string.Empty;
    public string Token_Type { get; set; } = string.Empty;
    public int Expires_In { get; set; }
    public string? Refresh_Token { get; set; }
    public string? Scope { get; set; }
}

public class SpotifyUserProfile
{
    public string Id { get; set; } = string.Empty;
    public string Display_Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<SpotifyImage> Images { get; set; } = new();
}

public class SpotifyImage
{
    public string Url { get; set; } = string.Empty;
}

public class SpotifyTopTracksResponse
{
    public List<SpotifyTrack> Items { get; set; } = new();
}

public class SpotifyTrack
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<SpotifyArtist> Artists { get; set; } = new();
    public SpotifyAlbum Album { get; set; } = new();
    public int Duration_Ms { get; set; }
}

public class SpotifyArtist
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class SpotifyAlbum
{
    public string Name { get; set; } = string.Empty;
    public List<SpotifyImage> Images { get; set; } = new();
}

public class SpotifyCurrentlyPlaying
{
    public bool Is_Playing { get; set; }
    public SpotifyTrack? Item { get; set; }
    public int Progress_Ms { get; set; }
}
