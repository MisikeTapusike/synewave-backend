using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.DTOs;
using Synewave.API.Services;

namespace Synewave.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RecommendationsController : ControllerBase
{
    private readonly IRecommendationService _rec;

    public RecommendationsController(IRecommendationService rec) => _rec = rec;

    /// <summary>Get personalized AI recommendations</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<RecommendationDto>>>> Get([FromQuery] int count = 6)
    {
        var userId = GetUserId();
        var recs = await _rec.GetRecommendationsAsync(userId, count);
        return Ok(new ApiResponse<List<RecommendationDto>>(true, recs));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db) => _db = db;

    /// <summary>Get all notifications for current user</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<NotificationDto>>>> GetAll()
    {
        var userId = GetUserId();
        var notifs = await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Message, n.ActionUrl, n.IsRead, n.CreatedAt))
            .ToListAsync();

        return Ok(new ApiResponse<List<NotificationDto>>(true, notifs));
    }

    /// <summary>Mark a notification as read</summary>
    [HttpPatch("{id}/read")]
    public async Task<ActionResult<ApiResponse<string>>> MarkRead(Guid id)
    {
        var userId = GetUserId();
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notif == null) return NotFound();

        notif.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok(new ApiResponse<string>(true, "Prečítané."));
    }

    /// <summary>Mark all notifications as read</summary>
    [HttpPatch("read-all")]
    public async Task<ActionResult<ApiResponse<string>>> MarkAllRead()
    {
        var userId = GetUserId();
        await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return Ok(new ApiResponse<string>(true, "Všetky prečítané."));
    }

    /// <summary>Get unread count</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<ApiResponse<int>>> GetUnreadCount()
    {
        var userId = GetUserId();
        var count = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
        return Ok(new ApiResponse<int>(true, count));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
