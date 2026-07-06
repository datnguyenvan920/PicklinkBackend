using System.Security.Claims;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Text;
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
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IGoogleAuthService googleAuthService,
        IEmailSender emailSender,
        ILogger<AuthController> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _googleAuthService = googleAuthService;
        _emailSender = emailSender;
        _logger = logger;
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
            Commune = string.IsNullOrWhiteSpace(request.Commune) ? null : request.Commune.Trim(),
            ProfileImageUrl = string.IsNullOrWhiteSpace(request.ProfileImageUrl)
                ? null
                : request.ProfileImageUrl.Trim()
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return Ok(CreateAuthResponse(user));
    }

    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = "Email này chưa được đăng ký. Vui lòng đăng ký tài khoản trước." });
        }

        var resetToken = GeneratePasswordResetToken();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(15);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var activeTokens = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.UsedAt == null &&
                            token.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.UsedAt = now;
        }

        var passwordResetToken = new PasswordResetToken
        {
            UserId = user.UserId,
            TokenHash = HashPasswordResetToken(resetToken),
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        _dbContext.PasswordResetTokens.Add(passwordResetToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailSender.SendPasswordResetCodeAsync(
                user.Email,
                user.Username,
                resetToken,
                expiresAt,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Password reset email is not configured.");

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Chức năng gửi email chưa được cấu hình. Vui lòng cấu hình SMTP cho máy chủ."
            });
        }
        catch (SmtpException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Could not send password reset email to {Email}.", user.Email);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Không thể gửi mã đặt lại mật khẩu qua email. Vui lòng thử lại sau."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Unexpected error while sending password reset email to {Email}.", user.Email);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Không thể gửi mã đặt lại mật khẩu qua email. Vui lòng kiểm tra cấu hình email và thử lại."
            });
        }

        await transaction.CommitAsync(cancellationToken);

        return Ok(new ForgotPasswordResponse
        {
            Message = "Mã đặt lại mật khẩu đã được gửi qua email. Vui lòng kiểm tra hộp thư và dùng mã trong vòng 15 phút.",
            ExpiresAt = expiresAt
        });
    }

    [HttpPost("verify-reset-code")]
    public async Task<ActionResult> VerifyResetCode(
        VerifyPasswordResetCodeRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashPasswordResetToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var isValid = await _dbContext.PasswordResetTokens
            .AsNoTracking()
            .AnyAsync(token =>
                token.User.Email == email &&
                token.TokenHash == tokenHash &&
                token.UsedAt == null &&
                token.ExpiresAt > now,
                cancellationToken);

        if (!isValid)
        {
            return BadRequest(new { message = "Mã xác thực không hợp lệ hoặc đã hết hạn." });
        }

        return Ok(new { message = "Mã xác thực hợp lệ." });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashPasswordResetToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (user is null)
        {
            return NotFound(new { message = "Email này chưa được đăng ký. Vui lòng đăng ký tài khoản trước." });
        }

        var resetToken = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.TokenHash == tokenHash &&
                            token.UsedAt == null)
            .OrderByDescending(token => token.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (resetToken is null || resetToken.ExpiresAt <= now)
        {
            return BadRequest(new { message = "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." });
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        resetToken.UsedAt = now;

        var otherActiveTokens = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.ResetTokenId != resetToken.ResetTokenId &&
                            token.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in otherActiveTokens)
        {
            activeToken.UsedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới." });
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

        if (user.IsLocked)
        {
            return Forbid();
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

        if (user.IsLocked)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(user.ProfileImageUrl) &&
            !string.IsNullOrWhiteSpace(googleUser.Picture))
        {
            user.ProfileImageUrl = googleUser.Picture.Trim();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(CreateAuthResponse(user));
    }

    [HttpPost("google/register")]
    public async Task<ActionResult<AuthResponse>> GoogleRegister(
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
            return Unauthorized(new { message = "Phiên đăng ký Google không hợp lệ. Vui lòng thử lại." });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(
                title: "Cấu hình đăng nhập Google chưa hợp lệ.",
                detail: exception.Message);
        }

        var email = googleUser.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return Conflict(new
            {
                message = "Email Google này đã được đăng ký. Vui lòng chọn đăng nhập bằng Google."
            });
        }

        var user = new User
        {
            Username = await CreateUniqueGoogleUsernameAsync(googleUser, cancellationToken),
            Email = email,
            PasswordHash = _passwordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
            UserType = "Player",
            ProfileImageUrl = string.IsNullOrWhiteSpace(googleUser.Picture)
                ? null
                : googleUser.Picture.Trim()
        };

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _dbContext.Players.Add(new Player
        {
            UserId = user.UserId,
            SkillLevel = 1,
            Prestige = 100
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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

    private async Task<string> CreateUniqueGoogleUsernameAsync(
        GoogleUserInfo googleUser,
        CancellationToken cancellationToken)
    {
        var baseName = string.IsNullOrWhiteSpace(googleUser.Name)
            ? googleUser.Email.Split('@')[0]
            : googleUser.Name.Trim();

        if (baseName.Length < 3)
        {
            baseName = $"Picklink {baseName}".Trim();
        }

        baseName = baseName[..Math.Min(baseName.Length, 90)];
        var candidate = baseName;

        while (await _dbContext.Users.AnyAsync(user => user.Username == candidate, cancellationToken))
        {
            candidate = $"{baseName}-{RandomNumberGenerator.GetInt32(1000, 10_000)}";
        }

        return candidate;
    }

    private static string GeneratePasswordResetToken()
    {
        return RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();
    }

    private static string HashPasswordResetToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Checks whether the currently authenticated user has been assigned a role.
    /// A user is considered to have no role yet when their UserType is still "User"
    /// (the default value set at registration). Any other value is treated as a real role.
    /// </summary>
    [Authorize]
    [HttpGet("role-status")]
    public async Task<ActionResult> RoleStatus()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var hasRole = !string.Equals(user.UserType, "User", StringComparison.OrdinalIgnoreCase);

        return Ok(new
        {
            hasRole,
            userType = user.UserType
        });
    }

    /// <summary>
    /// Assigns a role to the currently authenticated user.
    /// Can only be called once — users whose UserType is already set to something
    /// other than "User" will receive a 409 Conflict.
    ///
    /// Supported roles:
    ///   - "VenueOwner" : Updates UserType only.
    ///   - "Player"     : Updates UserType and creates a Player profile row.
    ///                    Requires the Experience field (Beginner / Intermediate / Advanced).
    ///   - "Staff"      : Updates UserType only (Staff profile creation is not yet implemented).
    /// </summary>
    [Authorize]
    [HttpPost("assign-role")]
    public async Task<ActionResult> AssignRole(AssignRoleRequest request)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        // Guard: role has already been assigned
        if (!string.Equals(user.UserType, "User", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { message = $"Role is already assigned as '{user.UserType}'." });
        }

        var role = request.Role.Trim();

        switch (role)
        {
            case "VenueOwner":
                user.UserType = "VenueOwner";
                break;

            case "Player":
                if (request.Experience is null)
                {
                    return BadRequest(new { message = "Experience is required when assigning the Player role." });
                }

                user.UserType = "Player";

                var player = new Player
                {
                    UserId = user.UserId,
                    SkillLevel = MapExperienceToSkillLevel(request.Experience.Value),
                    Prestige = 100
                };

                _dbContext.Players.Add(player);
                break;

            case "Staff":
                // Staff profile creation is not yet implemented.
                user.UserType = "Staff";
                break;

            default:
                return BadRequest(new { message = $"Unknown role '{role}'. Accepted values: Player, VenueOwner, Staff." });
        }

        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            message = $"Role '{role}' assigned successfully.",
            userType = user.UserType
        });
    }

    /// <summary>Maps the self-reported experience level to a numeric skill level.</summary>
    private static double MapExperienceToSkillLevel(ExperienceLevel experience) => experience switch
    {
        ExperienceLevel.Beginner     => 1.0,
        ExperienceLevel.Intermediate => 1.5,
        ExperienceLevel.Advanced     => 2.0,
        _                            => 1.0
    };
}
