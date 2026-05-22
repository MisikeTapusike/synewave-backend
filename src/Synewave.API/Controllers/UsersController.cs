using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.DTOs;
using Synewave.API.Models;
using Synewave.API.Services;

namespace Synewave.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IRecommendationService _rec;

    public UsersController(AppDbContext db, IRecommendationService rec)
    {
        _db = db;
        _rec = rec;
    }

    /// <summary>Get current user profile</summary>
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetMe()
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();
        return Ok(new ApiResponse<UserDto>(true, MapUser(user)));
    }

    /// <summary>Get user stats for the stats page</summary>
    [HttpGet("me/stats")]
    public async Task<ActionResult<ApiResponse<UserStatsDto>>> GetMyStats()
    {
        var userId = GetUserId();
        var cutoff30 = DateTime.UtcNow.AddDays(-30);

        var history = await _db.ListeningHistory
            .Where(h => h.UserId == userId && h.PlayedAt > cutoff30)
            .Include(h => h.Track)
            .ToListAsync();

        var topTracks = history
            .GroupBy(h => h.TrackId)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g =>
            {
                var t = g.First().Track;
                return new TrackDto(t.Id, t.Title, t.Artist, t.Album, t.CoverUrl, t.DurationSeconds, t.Genre, false);
            })
            .ToList();

        var topGenres = history
            .Where(h => h.Track.Genre != null)
            .GroupBy(h => h.Track.Genre!)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToList();

        // Daily activity for last 7 days
        var weekActivity = Enumerable.Range(0, 7)
            .Select(i => DateTime.UtcNow.AddDays(-6 + i))
            .Select(day => new DayActivityDto(
                day.ToString("ddd"),
                history
                    .Where(h => h.PlayedAt.Date == day.Date)
                    .Sum(h => h.SecondsListened) / 60
            ))
            .ToList();

        var stats = new UserStatsDto(
            TotalTracksPlayed: history.Select(h => h.TrackId).Distinct().Count(),
            TotalHoursListened: history.Sum(h => h.SecondsListened) / 3600,
            UniqueArtists: history.Select(h => h.Track.Artist).Distinct().Count(),
            UniqueGenres: history.Where(h => h.Track.Genre != null).Select(h => h.Track.Genre!).Distinct().Count(),
            TopTracks: topTracks,
            TopGenres: topGenres,
            WeeklyActivity: weekActivity
        );

        return Ok(new ApiResponse<UserStatsDto>(true, stats));
    }

    /// <summary>Search users by username</summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new ApiResponse<List<UserDto>>(false, null, "Hľadaný výraz musí mať aspoň 2 znaky."));

        var userId = GetUserId();
        var users = await _db.Users
            .Where(u => u.Id != userId && u.Username.ToLower().Contains(q.ToLower()))
            .Take(20)
            .Select(u => new UserDto(u.Id, u.Username, u.Email, u.AvatarUrl, u.LastActiveAt))
            .ToListAsync();

        return Ok(new ApiResponse<List<UserDto>>(true, users));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static UserDto MapUser(User u) =>
        new(u.Id, u.Username, u.Email, u.AvatarUrl, u.LastActiveAt);
}
