using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.DTOs;
using System.Security.Claims;

namespace Synewave.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly AppDbContext _db;
    public FriendsController(AppDbContext db) { _db = db; }

    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // GET /api/Friends — zoznam priatelov
    [HttpGet]
    public async Task<IActionResult> GetFriends()
    {
        var userId = UserId;
        var friends = await _db.Friendships
            .Where(f => (f.RequesterId == userId || f.AddresseeId == userId) && (int)f.Status == 1)
            .Select(f => new {
                UserId       = f.RequesterId == userId ? f.AddresseeId : f.RequesterId,
                FriendshipId = f.Id
            })
            .ToListAsync();

        var result = new List<object>();
        foreach (var f in friends)
        {
            var u = await _db.Users.FindAsync(f.UserId);
            if (u == null) continue;
            result.Add(new {
                id              = u.Id,
                friendId        = u.Id,
                friendshipId    = f.FriendshipId,
                username        = u.Username,
                friendUsername  = u.Username,
                avatarUrl       = u.AvatarUrl,
                isOnline        = u.LastActiveAt > DateTime.UtcNow.AddMinutes(-10),
                currentSong     = u.CurrentSong,
                currentArtist   = u.CurrentArtist,
                trackTitle      = u.CurrentSong,
                trackArtist     = u.CurrentArtist,
                lastPlayedAt    = u.LastActiveAt
            });
        }
        return Ok(new { success = true, data = result });
    }

    // GET /api/Friends/feed — priatelia ktori pocuvaju
    [HttpGet("feed")]
    public async Task<IActionResult> GetFeed()
    {
        var userId = UserId;
        var friendIds = await _db.Friendships
            .Where(f => (f.RequesterId == userId || f.AddresseeId == userId) && (int)f.Status == 1)
            .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId)
            .ToListAsync();

        var result = new List<object>();
        foreach (var fid in friendIds)
        {
            var u = await _db.Users.FindAsync(fid);
            if (u == null) continue;
            var song   = u.CurrentSong;
            var artist = u.CurrentArtist;
            if (string.IsNullOrEmpty(song)) continue; // len tí čo počúvajú
            result.Add(new {
                userId         = u.Id,
                username       = u.Username,
                friendUsername = u.Username,
                avatarUrl      = u.AvatarUrl,
                isOnline       = true,
                currentTrack   = new { title = song, artist = artist },
                trackTitle     = song,
                trackArtist    = artist,
                lastActiveAgo  = GetTimeAgo(u.LastActiveAt)
            });
        }
        return Ok(new { success = true, data = result });
    }

    // POST /api/Friends/request/{targetUserId}
    [HttpPost("request/{targetUserId}")]
    public async Task<IActionResult> SendRequest(Guid targetUserId)
    {
        var userId = UserId;
        if (userId == targetUserId)
            return BadRequest(new { error = "Nemôžeš si pridať sám seba." });

        var exists = await _db.Friendships.AnyAsync(f =>
            (f.RequesterId == userId && f.AddresseeId == targetUserId) ||
            (f.RequesterId == targetUserId && f.AddresseeId == userId));

        if (exists)
            return BadRequest(new { error = "Žiadosť už existuje." });

        var friendship = new Synewave.API.Models.Friendship
        {
            RequesterId = userId,
            AddresseeId = targetUserId,
            Status      = (Synewave.API.Models.FriendshipStatus)0,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        return Ok(new { success = true, data = "Žiadosť odoslaná." });
    }

    // PATCH /api/Friends/request/{friendshipId}
    [HttpPatch("request/{friendshipId}")]
    public async Task<IActionResult> RespondRequest(Guid friendshipId, [FromBody] RespondRequestDto dto)
    {
        var userId = UserId;
        var friendship = await _db.Friendships.FindAsync(friendshipId);

        if (friendship == null)
            return NotFound(new { error = "Žiadosť nenájdená." });

        if (friendship.AddresseeId != userId)
            return Forbid();

        friendship.Status = dto.Accepted ? (Synewave.API.Models.FriendshipStatus)1 : (Synewave.API.Models.FriendshipStatus)2;
        friendship.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { success = true, data = dto.Accepted ? "Priateľ pridaný." : "Žiadosť odmietnutá." });
    }

    // DELETE /api/Friends/{friendId}
    [HttpDelete("{friendId}")]
    public async Task<IActionResult> RemoveFriend(Guid friendId)
    {
        var userId = UserId;
        var friendship = await _db.Friendships.FirstOrDefaultAsync(f =>
            ((f.RequesterId == userId && f.AddresseeId == friendId) ||
             (f.RequesterId == friendId && f.AddresseeId == userId)) && (int)f.Status == 1);

        if (friendship == null)
            return NotFound(new { error = "Priateľstvo nenájdené." });

        _db.Friendships.Remove(friendship);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = "Priateľ odstránený." });
    }

    // GET /api/Friends/feed compatibility
    [HttpGet("{friendId}/compatibility")]
    public async Task<IActionResult> GetCompatibility(Guid friendId)
    {
        return Ok(new { success = true, data = new { compatibilityScore = new Random().Next(60, 99) } });
    }

    // GET /api/Friends/pending
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var userId = UserId;
        var pending = await _db.Friendships
            .Where(f => f.AddresseeId == userId && (int)f.Status == 0)
            .ToListAsync();

        var result = new List<object>();
        foreach (var f in pending)
        {
            var u = await _db.Users.FindAsync(f.RequesterId);
            if (u == null) continue;
            result.Add(new {
                friendshipId   = f.Id,
                id             = f.Id,
                userId         = u.Id,
                username       = u.Username,
                friendUsername = u.Username,
                avatarUrl      = u.AvatarUrl
            });
        }
        return Ok(new { success = true, data = result });
    }

    private static string GetTimeAgo(DateTime? dt)
    {
        if (dt == null) return "dávno";
        var diff = DateTime.UtcNow - dt.Value;
        if (diff.TotalMinutes < 1)  return "práve teraz";
        if (diff.TotalMinutes < 60) return $"pred {(int)diff.TotalMinutes} min";
        if (diff.TotalHours   < 24) return $"pred {(int)diff.TotalHours} h";
        return $"pred {(int)diff.TotalDays} d";
    }
}

public class RespondRequestDto
{
    public bool Accepted { get; set; }
}
