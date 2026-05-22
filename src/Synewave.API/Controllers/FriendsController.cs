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
public class FriendsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IRecommendationService _rec;

    public FriendsController(AppDbContext db, IRecommendationService rec)
    {
        _db = db;
        _rec = rec;
    }

    /// <summary>Get all friends with their current activity</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<FriendActivityDto>>>> GetFriends()
    {
        var userId = GetUserId();
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-10);

        var friendships = await _db.Friendships
            .Where(f => (f.RequesterId == userId || f.AddresseeId == userId)
                     && f.Status == FriendshipStatus.Accepted)
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .ToListAsync();

        var result = new List<FriendActivityDto>();

        foreach (var fs in friendships)
        {
            var friend = fs.RequesterId == userId ? fs.Addressee : fs.Requester;

            // Get latest track
            var latest = await _db.ListeningHistory
                .Where(h => h.UserId == friend.Id)
                .Include(h => h.Track)
                .OrderByDescending(h => h.PlayedAt)
                .FirstOrDefaultAsync();

            result.Add(new FriendActivityDto(
                UserId: friend.Id,
                Username: friend.Username,
                AvatarUrl: friend.AvatarUrl,
                CurrentSong: latest?.Track.Title,
                CurrentArtist: latest?.Track.Artist,
                LastPlayedAt: latest?.PlayedAt,
                IsOnline: friend.LastActiveAt > onlineThreshold
            ));
        }

        return Ok(new ApiResponse<List<FriendActivityDto>>(true,
            result.OrderByDescending(f => f.IsOnline).ThenByDescending(f => f.LastPlayedAt).ToList()));
    }

    /// <summary>Send a friend request</summary>
    [HttpPost("request/{targetUserId}")]
    public async Task<ActionResult<ApiResponse<string>>> SendRequest(Guid targetUserId)
    {
        var userId = GetUserId();
        if (userId == targetUserId)
            return BadRequest(new ApiResponse<string>(false, null, "Nemôžeš pridať sám seba."));

        var exists = await _db.Friendships.AnyAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == userId));

        if (exists)
            return Conflict(new ApiResponse<string>(false, null, "Žiadosť už existuje."));

        _db.Friendships.Add(new Friendship
        {
            RequesterId = userId,
            AddresseeId = targetUserId,
            Status = FriendshipStatus.Pending
        });

        // Create notification
        _db.Notifications.Add(new Notification
        {
            UserId = targetUserId,
            Type = "friend_request",
            Message = "Ti poslal/a žiadosť o priateľstvo.",
            ActionUrl = $"/friends/{userId}"
        });

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<string>(true, "Žiadosť odoslaná."));
    }

    /// <summary>Accept or reject a friend request</summary>
    [HttpPatch("request/{friendshipId}")]
    public async Task<ActionResult<ApiResponse<string>>> RespondToRequest(
        Guid friendshipId, [FromQuery] string action)
    {
        var userId = GetUserId();
        var fs = await _db.Friendships.FindAsync(friendshipId);

        if (fs == null || fs.AddresseeId != userId)
            return NotFound();

        fs.Status = action == "accept" ? FriendshipStatus.Accepted : FriendshipStatus.Rejected;
        fs.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new ApiResponse<string>(true,
            action == "accept" ? "Priateľstvo prijaté!" : "Žiadosť odmietnutá."));
    }

    /// <summary>Remove a friend</summary>
    [HttpDelete("{friendId}")]
    public async Task<ActionResult<ApiResponse<string>>> RemoveFriend(Guid friendId)
    {
        var userId = GetUserId();
        var fs = await _db.Friendships.FirstOrDefaultAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == friendId) ||
            (f.RequesterId == friendId && f.AddresseeId == userId));

        if (fs == null) return NotFound();

        _db.Friendships.Remove(fs);
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<string>(true, "Priateľ odstránený."));
    }

    /// <summary>Get friends feed — what they recently played</summary>
    [HttpGet("feed")]
    public async Task<ActionResult<ApiResponse<List<FeedItemDto>>>> GetFeed([FromQuery] int page = 1)
    {
        var userId = GetUserId();
        var friendIds = await GetFriendIdsAsync(userId);

        var items = await _db.ListeningHistory
            .Where(h => friendIds.Contains(h.UserId) && h.PlayedAt > DateTime.UtcNow.AddDays(-7))
            .Include(h => h.Track)
            .Include(h => h.User)
            .OrderByDescending(h => h.PlayedAt)
            .Skip((page - 1) * 20)
            .Take(20)
            .Select(h => new FeedItemDto(
                h.UserId,
                h.User.Username,
                h.User.AvatarUrl,
                new TrackDto(h.Track.Id, h.Track.Title, h.Track.Artist, h.Track.Album,
                             h.Track.CoverUrl, h.Track.DurationSeconds, h.Track.Genre, false),
                h.PlayedAt,
                FormatAgo(h.PlayedAt)
            ))
            .ToListAsync();

        return Ok(new ApiResponse<List<FeedItemDto>>(true, items));
    }

    /// <summary>Get compatibility score with a friend</summary>
    [HttpGet("{friendId}/compatibility")]
    public async Task<ActionResult<ApiResponse<int>>> GetCompatibility(Guid friendId)
    {
        var userId = GetUserId();
        var score = await _rec.GetCompatibilityScoreAsync(userId, friendId);
        return Ok(new ApiResponse<int>(true, score));
    }

    private async Task<List<Guid>> GetFriendIdsAsync(Guid userId)
    {
        var sent = await _db.Friendships
            .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.AddresseeId).ToListAsync();
        var received = await _db.Friendships
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.RequesterId).ToListAsync();
        return sent.Concat(received).Distinct().ToList();
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string FormatAgo(DateTime dt)
    {
        var diff = DateTime.UtcNow - dt;
        if (diff.TotalMinutes < 2) return "práve teraz";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h";
        return $"{(int)diff.TotalDays}d";
    }
}
