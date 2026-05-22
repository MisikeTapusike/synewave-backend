using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.DTOs;
using Synewave.API.Models;

namespace Synewave.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TracksController : ControllerBase
{
    private readonly AppDbContext _db;

    public TracksController(AppDbContext db) => _db = db;

    /// <summary>Search tracks</summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<TrackDto>>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new ApiResponse<List<TrackDto>>(false, null, "Zadaj hľadaný výraz."));

        var userId = GetUserId();
        var likedIds = await _db.Likes.Where(l => l.UserId == userId).Select(l => l.TrackId).ToListAsync();

        var tracks = await _db.Tracks
            .Where(t => t.Title.ToLower().Contains(q.ToLower())
                     || t.Artist.ToLower().Contains(q.ToLower()))
            .Take(20)
            .ToListAsync();

        return Ok(new ApiResponse<List<TrackDto>>(true,
            tracks.Select(t => MapTrack(t, likedIds.Contains(t.Id))).ToList()));
    }

    /// <summary>Log a played track (adds to listening history)</summary>
    [HttpPost("log")]
    public async Task<ActionResult<ApiResponse<string>>> LogPlay([FromBody] LogPlayRequest req)
    {
        var userId = GetUserId();
        var track = await _db.Tracks.FindAsync(req.TrackId);
        if (track == null) return NotFound(new ApiResponse<string>(false, null, "Skladba nenájdená."));

        _db.ListeningHistory.Add(new ListeningHistory
        {
            UserId = userId,
            TrackId = req.TrackId,
            SecondsListened = req.SecondsListened,
            Source = req.Source
        });

        // Update user's last active
        var user = await _db.Users.FindAsync(userId);
        if (user != null) user.LastActiveAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<string>(true, "Zaznamenaná."));
    }

    /// <summary>Create a new track (or find existing by SpotifyId)</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<TrackDto>>> Create([FromBody] CreateTrackRequest req)
    {
        // Check if already exists by Spotify ID
        if (!string.IsNullOrEmpty(req.SpotifyId))
        {
            var existing = await _db.Tracks.FirstOrDefaultAsync(t => t.SpotifyId == req.SpotifyId);
            if (existing != null)
                return Ok(new ApiResponse<TrackDto>(true, MapTrack(existing, false)));
        }

        var track = new Track
        {
            Title = req.Title,
            Artist = req.Artist,
            Album = req.Album,
            CoverUrl = req.CoverUrl,
            SpotifyId = req.SpotifyId,
            DurationSeconds = req.DurationSeconds,
            Genre = req.Genre
        };

        _db.Tracks.Add(track);
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<TrackDto>(true, MapTrack(track, false)));
    }

    /// <summary>Like or unlike a track</summary>
    [HttpPost("{trackId}/like")]
    public async Task<ActionResult<ApiResponse<bool>>> ToggleLike(Guid trackId)
    {
        var userId = GetUserId();
        var like = await _db.Likes.FirstOrDefaultAsync(l => l.UserId == userId && l.TrackId == trackId);

        if (like != null)
        {
            _db.Likes.Remove(like);
            await _db.SaveChangesAsync();
            return Ok(new ApiResponse<bool>(true, false)); // now unliked
        }

        _db.Likes.Add(new Like { UserId = userId, TrackId = trackId });
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<bool>(true, true)); // now liked
    }

    /// <summary>Get user's liked tracks</summary>
    [HttpGet("liked")]
    public async Task<ActionResult<ApiResponse<List<TrackDto>>>> GetLiked()
    {
        var userId = GetUserId();
        var tracks = await _db.Likes
            .Where(l => l.UserId == userId)
            .Include(l => l.Track)
            .OrderByDescending(l => l.LikedAt)
            .Select(l => l.Track)
            .ToListAsync();

        return Ok(new ApiResponse<List<TrackDto>>(true,
            tracks.Select(t => MapTrack(t, true)).ToList()));
    }

    /// <summary>Get user's listening history</summary>
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<List<ListeningHistoryDto>>>> GetHistory(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        var likedIds = await _db.Likes.Where(l => l.UserId == userId).Select(l => l.TrackId).ToListAsync();

        var items = await _db.ListeningHistory
            .Where(h => h.UserId == userId)
            .Include(h => h.Track)
            .OrderByDescending(h => h.PlayedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new ListeningHistoryDto(
                h.Id,
                MapTrack(h.Track, likedIds.Contains(h.TrackId)),
                h.PlayedAt,
                h.SecondsListened
            ))
            .ToListAsync();

        return Ok(new ApiResponse<List<ListeningHistoryDto>>(true, items));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static TrackDto MapTrack(Track t, bool liked) =>
        new(t.Id, t.Title, t.Artist, t.Album, t.CoverUrl, t.DurationSeconds, t.Genre, liked);
}
