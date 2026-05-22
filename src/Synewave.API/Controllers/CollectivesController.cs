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
public class CollectivesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CollectivesController(AppDbContext db) => _db = db;

    /// <summary>Get all collectives the current user is in</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CollectiveDto>>>> GetMyCollectives()
    {
        var userId = GetUserId();
        var collectives = await _db.CollectiveMembers
            .Where(m => m.UserId == userId)
            .Include(m => m.Collective)
            .ThenInclude(c => c.Members)
            .ThenInclude(m => m.User)
            .Select(m => m.Collective)
            .ToListAsync();

        return Ok(new ApiResponse<List<CollectiveDto>>(true,
            collectives.Select(MapCollective).ToList()));
    }

    /// <summary>Get a single collective</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CollectiveDto>>> GetCollective(Guid id)
    {
        var collective = await _db.Collectives
            .Include(c => c.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (collective == null) return NotFound();
        return Ok(new ApiResponse<CollectiveDto>(true, MapCollective(collective)));
    }

    /// <summary>Create a new collective</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CollectiveDto>>> Create([FromBody] CreateCollectiveRequest req)
    {
        var userId = GetUserId();

        var collective = new Collective
        {
            Name = req.Name,
            Description = req.Description,
            Emoji = req.Emoji,
            Color = req.Color,
            CreatedByUserId = userId
        };
        _db.Collectives.Add(collective);

        // Add creator as admin
        _db.CollectiveMembers.Add(new CollectiveMember
        {
            CollectiveId = collective.Id,
            UserId = userId,
            Role = CollectiveRole.Admin
        });

        // Invite other users
        foreach (var invitedId in req.InvitedUserIds.Distinct().Where(id => id != userId))
        {
            _db.CollectiveMembers.Add(new CollectiveMember
            {
                CollectiveId = collective.Id,
                UserId = invitedId,
                Role = CollectiveRole.Member
            });

            _db.Notifications.Add(new Notification
            {
                UserId = invitedId,
                Type = "collective_invite",
                Message = $"Bol/a si pridaný/á do kolektívu \"{req.Name}\".",
                ActionUrl = $"/collectives/{collective.Id}"
            });
        }

        await _db.SaveChangesAsync();

        var created = await _db.Collectives
            .Include(c => c.Members).ThenInclude(m => m.User)
            .FirstAsync(c => c.Id == collective.Id);

        return Ok(new ApiResponse<CollectiveDto>(true, MapCollective(created)));
    }

    /// <summary>Add a member to collective (admin only)</summary>
    [HttpPost("{id}/members/{targetUserId}")]
    public async Task<ActionResult<ApiResponse<string>>> AddMember(Guid id, Guid targetUserId)
    {
        var userId = GetUserId();
        var isAdmin = await _db.CollectiveMembers
            .AnyAsync(m => m.CollectiveId == id && m.UserId == userId && m.Role == CollectiveRole.Admin);

        if (!isAdmin) return Forbid();

        var alreadyMember = await _db.CollectiveMembers
            .AnyAsync(m => m.CollectiveId == id && m.UserId == targetUserId);

        if (alreadyMember)
            return Conflict(new ApiResponse<string>(false, null, "Používateľ je už členom."));

        _db.CollectiveMembers.Add(new CollectiveMember
        {
            CollectiveId = id,
            UserId = targetUserId
        });

        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<string>(true, "Člen pridaný."));
    }

    /// <summary>Leave a collective</summary>
    [HttpDelete("{id}/leave")]
    public async Task<ActionResult<ApiResponse<string>>> Leave(Guid id)
    {
        var userId = GetUserId();
        var member = await _db.CollectiveMembers
            .FirstOrDefaultAsync(m => m.CollectiveId == id && m.UserId == userId);

        if (member == null) return NotFound();

        _db.CollectiveMembers.Remove(member);
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<string>(true, "Opustil/a si kolektív."));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static CollectiveDto MapCollective(Collective c) => new(
        c.Id, c.Name, c.Description, c.Emoji, c.Color,
        c.Members.Count,
        c.Members.Select(m => new UserPublicDto(
            m.User.Id, m.User.Username, m.User.AvatarUrl,
            m.User.LastActiveAt > DateTime.UtcNow.AddMinutes(-10),
            null, 0
        )).ToList()
    );
}
