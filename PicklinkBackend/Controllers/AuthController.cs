using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IGoogleAuthService _googleAuthService;

    public AuthController(
        ApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IGoogleAuthService googleAuthService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _googleAuthService = googleAuthService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _dbContext.Users.AnyAsync(user => user.Email == email))
        {
            return Conflict(new { message = "Email này đã được đăng ký." });
        }

        if (await _dbContext.Users.AnyAsync(user => user.Username == username))
        {
            return Conflict(new { message = "Tên người dùng này đã được sử dụng." });
        }

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            UserType = "User",
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            ProfileImageUrl = string.IsNullOrWhiteSpace(request.ProfileImageUrl)
                ? null
                : request.ProfileImageUrl.Trim()
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return Ok(CreateAuthResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email);

        if (user is null)
        {
            return NotFound(new
            {
                message = "Email này chưa được đăng ký. Vui lòng đăng ký tài khoản trước."
            });
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });
        }

        return Ok(CreateAuthResponse(user));
    }

    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        GoogleUserInfo googleUser;
        try
        {
            googleUser = await _googleAuthService.VerifyIdTokenAsync(request.IdToken, cancellationToken);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return Unauthorized(new { message = "Phiên đăng nhập Google không hợp lệ. Vui lòng thử lại." });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(
                title: "Cấu hình đăng nhập Google chưa hợp lệ.",
                detail: exception.Message);
        }

        var email = googleUser.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
        {
            return NotFound(new
            {
                message = "Email Google này chưa được đăng ký. Vui lòng đăng ký tài khoản trước."
            });
        }

        if (string.IsNullOrWhiteSpace(user.ProfileImageUrl) &&
            !string.IsNullOrWhiteSpace(googleUser.Picture))
        {
            user.ProfileImageUrl = googleUser.Picture.Trim();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(CreateAuthResponse(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(UserResponse.FromUser(user));
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var tokenResult = _jwtTokenService.GenerateToken(user);

        return new AuthResponse
        {
            Token = tokenResult.Token,
            ExpiresAt = tokenResult.ExpiresAt,
            User = UserResponse.FromUser(user)
        };
    }
}
