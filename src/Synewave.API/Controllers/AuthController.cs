using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.DTOs;
using Synewave.API.Models;
using Synewave.API.Services;

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

    /// <summary>Register a new user</summary>
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new ApiResponse<AuthResponse>(false, null, "Email je už obsadený."));

        if (await _db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new ApiResponse<AuthResponse>(false, null, "Používateľské meno je obsadené."));

        var user = new User
        {
            Username = req.Username.Trim(),
            Email = req.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _auth.GenerateToken(user);
        var dto = MapUser(user);
        return Ok(new ApiResponse<AuthResponse>(true, new AuthResponse(token, dto)));
    }

    /// <summary>Login with email + password</summary>
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.ToLower());

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new ApiResponse<AuthResponse>(false, null, "Nesprávny email alebo heslo."));

        user.LastActiveAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var token = _auth.GenerateToken(user);
        return Ok(new ApiResponse<AuthResponse>(true, new AuthResponse(token, MapUser(user))));
    }

    private static UserDto MapUser(User u) =>
        new(u.Id, u.Username, u.Email, u.AvatarUrl, u.LastActiveAt);
}
