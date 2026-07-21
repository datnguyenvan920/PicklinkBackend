using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Infrastructure;

namespace PicklinkBackend.Services.Auth;

public sealed class AuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        ApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IGoogleAuthService googleAuthService,
        IEmailSender emailSender,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _googleAuthService = googleAuthService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<AuthServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
            return AuthServiceResult<AuthResponse>.Conflict("Email nay da duoc dang ky.");

        if (await _dbContext.Users.AnyAsync(user => user.Username == username, cancellationToken))
            return AuthServiceResult<AuthResponse>.Conflict("Ten nguoi dung nay da duoc su dung.");

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
        await _dbContext.SaveChangesAsync(cancellationToken);

        return AuthServiceResult<AuthResponse>.Success(CreateAuthResponse(user));
    }

    public async Task<AuthServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            return AuthServiceResult<AuthResponse>.Unauthorized("Email hoặc mật khẩu không đúng.");

        if (user.IsLocked)
            return AuthServiceResult<AuthResponse>.Forbidden();

        return AuthServiceResult<AuthResponse>.Success(CreateAuthResponse(user));
    }

    public async Task<AuthServiceResult<AuthResponse>> GoogleLoginAsync(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var googleResult = await VerifyGoogleTokenAsync(request.IdToken, isRegister: false, cancellationToken);
        if (googleResult.Status != AuthServiceResultStatus.Success)
            return AuthServiceResult<AuthResponse>.From(googleResult);

        var googleUser = googleResult.Value!;
        var email = googleUser.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
            return AuthServiceResult<AuthResponse>.NotFound("Email Google nay chua duoc dang ky. Vui long dang ky tai khoan truoc.");

        if (user.IsLocked)
            return AuthServiceResult<AuthResponse>.Forbidden();

        if (string.IsNullOrWhiteSpace(user.ProfileImageUrl) &&
            !string.IsNullOrWhiteSpace(googleUser.Picture))
        {
            user.ProfileImageUrl = googleUser.Picture.Trim();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return AuthServiceResult<AuthResponse>.Success(CreateAuthResponse(user));
    }

    public async Task<AuthServiceResult<AuthResponse>> GoogleRegisterAsync(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var googleResult = await VerifyGoogleTokenAsync(request.IdToken, isRegister: true, cancellationToken);
        if (googleResult.Status != AuthServiceResultStatus.Success)
            return AuthServiceResult<AuthResponse>.From(googleResult);

        var googleUser = googleResult.Value!;
        var email = googleUser.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Users.AnyAsync(user => user.Email == email, cancellationToken))
            return AuthServiceResult<AuthResponse>.Conflict("Email Google nay da duoc dang ky. Vui long chon dang nhap bang Google.");

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

        return AuthServiceResult<AuthResponse>.Success(CreateAuthResponse(user));
    }

    public async Task<AuthServiceResult<ForgotPasswordResponse>> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(15);
        var response = CreateForgotPasswordResponse(expiresAt);
        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

        if (user is null)
            return AuthServiceResult<ForgotPasswordResponse>.Success(response);

        var resetToken = GeneratePasswordResetToken();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var activeTokens = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.UsedAt == null &&
                            token.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
            activeToken.UsedAt = now;

        _dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.UserId,
            TokenHash = HashPasswordResetToken(resetToken),
            CreatedAt = now,
            ExpiresAt = expiresAt
        });

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
            return AuthServiceResult<ForgotPasswordResponse>.ServerError("Chuc nang gui email chua duoc cau hinh. Vui long cau hinh SMTP cho may chu.");
        }
        catch (SmtpException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Could not send password reset email to {Email}.", user.Email);
            return AuthServiceResult<ForgotPasswordResponse>.ServerError("Khong the gui ma dat lai mat khau qua email. Vui long thu lai sau.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Unexpected error while sending password reset email to {Email}.", user.Email);
            return AuthServiceResult<ForgotPasswordResponse>.ServerError("Khong the gui ma dat lai mat khau qua email. Vui long kiem tra cau hinh email va thu lai.");
        }

        await transaction.CommitAsync(cancellationToken);

        return AuthServiceResult<ForgotPasswordResponse>.Success(response);
    }

    public async Task<AuthServiceResult<object>> VerifyResetCodeAsync(
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

        return isValid
            ? AuthServiceResult<object>.Success(new { message = "Ma xac thuc hop le." })
            : AuthServiceResult<object>.BadRequest("Ma xac thuc khong hop le hoac da het han.");
    }

    public async Task<AuthServiceResult<object>> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashPasswordResetToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var user = await _dbContext.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (user is null)
            return AuthServiceResult<object>.BadRequest("Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");

        var resetToken = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.TokenHash == tokenHash &&
                            token.UsedAt == null)
            .OrderByDescending(token => token.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (resetToken is null || resetToken.ExpiresAt <= now)
            return AuthServiceResult<object>.BadRequest("Ma dat lai mat khau khong hop le hoac da het han.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        resetToken.UsedAt = now;

        var otherActiveTokens = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.UserId &&
                            token.ResetTokenId != resetToken.ResetTokenId &&
                            token.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in otherActiveTokens)
            activeToken.UsedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return AuthServiceResult<object>.Success(new { message = "Dat lai mat khau thanh cong. Vui long dang nhap bang mat khau moi." });
    }

    public async Task<AuthServiceResult<object>> GetRoleStatusAsync(int? userId, CancellationToken cancellationToken)
    {
        if (userId is null) return AuthServiceResult<object>.Unauthorized();

        var user = await _dbContext.Users.FindAsync([userId.Value], cancellationToken);
        if (user is null) return AuthServiceResult<object>.NotFound("User not found.");

        var hasRole = !string.Equals(user.UserType, "User", StringComparison.OrdinalIgnoreCase);
        return AuthServiceResult<object>.Success(new
        {
            hasRole,
            userType = user.UserType
        });
    }

    public async Task<AuthServiceResult<object>> AssignRoleAsync(
        int? userId,
        AssignRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null) return AuthServiceResult<object>.Unauthorized();

        var user = await _dbContext.Users.FindAsync([userId.Value], cancellationToken);
        if (user is null) return AuthServiceResult<object>.NotFound("User not found.");

        if (!string.Equals(user.UserType, "User", StringComparison.OrdinalIgnoreCase))
            return AuthServiceResult<object>.Conflict($"Role is already assigned as '{user.UserType}'.");

        var role = request.Role.Trim();
        switch (role)
        {
            case "VenueOwner":
                user.UserType = "VenueOwner";
                break;

            case "Player":
                if (request.Experience is null)
                    return AuthServiceResult<object>.BadRequest("Experience is required when assigning the Player role.");

                user.UserType = "Player";
                _dbContext.Players.Add(new Player
                {
                    UserId = user.UserId,
                    SkillLevel = MapExperienceToSkillLevel(request.Experience.Value),
                    Prestige = 100
                });
                break;

            default:
                return AuthServiceResult<object>.BadRequest($"Unknown role '{role}'. Accepted values: Player, VenueOwner.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return AuthServiceResult<object>.Success(new
        {
            message = $"Role '{role}' assigned successfully.",
            userType = user.UserType
        });
    }

    public async Task<AuthServiceResult<UserResponse>> GetMeAsync(int? userId, CancellationToken cancellationToken)
    {
        if (userId is null) return AuthServiceResult<UserResponse>.Unauthorized();

        var user = await _dbContext.Users.FindAsync([userId.Value], cancellationToken);
        return user is null
            ? AuthServiceResult<UserResponse>.NotFound()
            : AuthServiceResult<UserResponse>.Success(UserResponse.FromUser(user));
    }

    private async Task<AuthServiceResult<GoogleUserInfo>> VerifyGoogleTokenAsync(
        string idToken,
        bool isRegister,
        CancellationToken cancellationToken)
    {
        try
        {
            return AuthServiceResult<GoogleUserInfo>.Success(
                await _googleAuthService.VerifyIdTokenAsync(idToken, cancellationToken));
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return AuthServiceResult<GoogleUserInfo>.Unauthorized(
                isRegister
                    ? "Phien dang ky Google khong hop le. Vui long thu lai."
                    : "Phien dang nhap Google khong hop le. Vui long thu lai.");
        }
        catch (InvalidOperationException exception)
        {
            return AuthServiceResult<GoogleUserInfo>.Problem(
                "Cau hinh dang nhap Google chua hop le.",
                exception.Message);
        }
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
            baseName = $"Picklink {baseName}".Trim();

        baseName = baseName[..Math.Min(baseName.Length, 90)];
        var candidate = baseName;

        while (await _dbContext.Users.AnyAsync(user => user.Username == candidate, cancellationToken))
            candidate = $"{baseName}-{RandomNumberGenerator.GetInt32(1000, 10_000)}";

        return candidate;
    }

    private static ForgotPasswordResponse CreateForgotPasswordResponse(DateTime expiresAt) => new()
    {
        Message = "Nếu email đã được đăng ký, mã đặt lại mật khẩu sẽ được gửi và có hiệu lực trong 15 phút.",
        ExpiresAt = expiresAt
    };

    private static string GeneratePasswordResetToken() =>
        RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString();

    private static string HashPasswordResetToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    private static double MapExperienceToSkillLevel(ExperienceLevel experience) => experience switch
    {
        ExperienceLevel.Beginner => 1.0,
        ExperienceLevel.Intermediate => 1.5,
        ExperienceLevel.Advanced => 2.0,
        _ => 1.0
    };
}

public sealed record AuthServiceResult<T>(
    AuthServiceResultStatus Status,
    T? Value = default,
    string? ErrorMessage = null,
    string? Title = null)
{
    public static AuthServiceResult<T> Success(T value) =>
        new(AuthServiceResultStatus.Success, value);

    public static AuthServiceResult<T> BadRequest(string errorMessage) =>
        new(AuthServiceResultStatus.BadRequest, ErrorMessage: errorMessage);

    public static AuthServiceResult<T> Unauthorized(string? errorMessage = null) =>
        new(AuthServiceResultStatus.Unauthorized, ErrorMessage: errorMessage);

    public static AuthServiceResult<T> Forbidden() =>
        new(AuthServiceResultStatus.Forbidden);

    public static AuthServiceResult<T> NotFound(string? errorMessage = null) =>
        new(AuthServiceResultStatus.NotFound, ErrorMessage: errorMessage);

    public static AuthServiceResult<T> Conflict(string errorMessage) =>
        new(AuthServiceResultStatus.Conflict, ErrorMessage: errorMessage);

    public static AuthServiceResult<T> ServerError(string errorMessage) =>
        new(AuthServiceResultStatus.ServerError, ErrorMessage: errorMessage);

    public static AuthServiceResult<T> Problem(string title, string detail) =>
        new(AuthServiceResultStatus.Problem, ErrorMessage: detail, Title: title);

    public static AuthServiceResult<T> From<TSource>(AuthServiceResult<TSource> result) =>
        new(result.Status, ErrorMessage: result.ErrorMessage, Title: result.Title);
}

public enum AuthServiceResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    ServerError,
    Problem
}
