using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Auth;
using PicklinkBackend.Services.Infrastructure;

namespace PicklinkBackend.Services.Auth.Implementations;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IGoogleAuthService googleAuthService,
        IEmailSender emailSender,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
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

        if (await _userRepository.ExistsByEmailAsync(email, cancellationToken))
            return AuthServiceResult<AuthResponse>.Conflict("Email này đã được đăng ký.");

        if (await _userRepository.ExistsByUsernameAsync(username, cancellationToken))
            return AuthServiceResult<AuthResponse>.Conflict("Tên người dùng này đã được sử dụng.");

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

        await _userRepository.AddUserAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return AuthServiceResult<AuthResponse>.Success(CreateAuthResponse(user));
    }

    public async Task<AuthServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

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
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null)
            return AuthServiceResult<AuthResponse>.NotFound("Email Google này chưa được đăng ký. Vui lòng đăng ký tài khoản trước.");

        if (user.IsLocked)
            return AuthServiceResult<AuthResponse>.Forbidden();

        if (string.IsNullOrWhiteSpace(user.ProfileImageUrl) &&
            !string.IsNullOrWhiteSpace(googleUser.Picture))
        {
            user.ProfileImageUrl = googleUser.Picture.Trim();
            await _userRepository.SaveChangesAsync(cancellationToken);
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
        if (await _userRepository.ExistsByEmailAsync(email, cancellationToken))
            return AuthServiceResult<AuthResponse>.Conflict("Email Google này đã được đăng ký. Vui lòng chọn đăng nhập bằng Google.");

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

        await using var transaction = await _userRepository.BeginTransactionAsync(cancellationToken);

        await _userRepository.AddUserAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        await _userRepository.AddPlayerAsync(new Player
        {
            UserId = user.UserId,
            SkillLevel = 1,
            Prestige = 5
        }, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);
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
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);

        if (user is null)
            return AuthServiceResult<ForgotPasswordResponse>.Success(response);

        var resetToken = GeneratePasswordResetToken();

        await using var transaction = await _userRepository.BeginTransactionAsync(cancellationToken);

        var activeTokens = await _userRepository.GetActivePasswordResetTokensAsync(user.UserId, now, cancellationToken);

        foreach (var activeToken in activeTokens)
            activeToken.UsedAt = now;

        await _userRepository.AddPasswordResetTokenAsync(new PasswordResetToken
        {
            UserId = user.UserId,
            TokenHash = HashPasswordResetToken(resetToken),
            CreatedAt = now,
            ExpiresAt = expiresAt
        }, cancellationToken);

        await _userRepository.SaveChangesAsync(cancellationToken);

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
            return AuthServiceResult<ForgotPasswordResponse>.ServerError("Chức năng gửi email chưa được cấu hình. Vui lòng cấu hình SMTP cho máy chủ.");
        }
        catch (SmtpException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Could not send password reset email to {Email}.", user.Email);
            return AuthServiceResult<ForgotPasswordResponse>.ServerError("Không thể gửi mã đặt lại mật khẩu qua email. Vui lòng thử lại sau.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(exception, "Unexpected error while sending password reset email to {Email}.", user.Email);
            return AuthServiceResult<ForgotPasswordResponse>.ServerError("Không thể gửi mã đặt lại mật khẩu qua email. Vui lòng kiểm tra cấu hình email và thử lại.");
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

        var isValid = await _userRepository.IsPasswordResetTokenValidAsync(email, tokenHash, now, cancellationToken);

        return isValid
            ? AuthServiceResult<object>.Success(new { message = "Mã xác thực hợp lệ." })
            : AuthServiceResult<object>.BadRequest("Mã xác thực không hợp lệ hoặc đã hết hạn.");
    }

    public async Task<AuthServiceResult<object>> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashPasswordResetToken(request.Token.Trim());
        var now = DateTime.UtcNow;

        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
            return AuthServiceResult<object>.BadRequest("Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");

        var resetToken = await _userRepository.GetValidPasswordResetTokenAsync(user.UserId, tokenHash, cancellationToken);

        if (resetToken is null || resetToken.ExpiresAt <= now)
            return AuthServiceResult<object>.BadRequest("Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        resetToken.UsedAt = now;

        var activeTokens = await _userRepository.GetActivePasswordResetTokensAsync(user.UserId, now, cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            if (activeToken.ResetTokenId != resetToken.ResetTokenId)
                activeToken.UsedAt = now;
        }

        await _userRepository.SaveChangesAsync(cancellationToken);

        return AuthServiceResult<object>.Success(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới." });
    }

    public async Task<AuthServiceResult<object>> GetRoleStatusAsync(int? userId, CancellationToken cancellationToken)
    {
        if (userId is null) return AuthServiceResult<object>.Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null) return AuthServiceResult<object>.NotFound("Không tìm thấy người dùng.");

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

        var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
        if (user is null) return AuthServiceResult<object>.NotFound("Không tìm thấy người dùng.");

        if (!string.Equals(user.UserType, "User", StringComparison.OrdinalIgnoreCase))
            return AuthServiceResult<object>.Conflict($"Tài khoản đã được gán vai trò '{user.UserType}'.");

        var role = request.Role.Trim();
        switch (role)
        {
            case "VenueOwner":
                user.UserType = "VenueOwner";
                break;

            case "Player":
                if (request.Experience is null)
                    return AuthServiceResult<object>.BadRequest("Vui lòng nhập kinh nghiệm khi chọn vai trò Người chơi.");

                user.UserType = "Player";
                await _userRepository.AddPlayerAsync(new Player
                {
                    UserId = user.UserId,
                    SkillLevel = MapExperienceToSkillLevel(request.Experience.Value),
                    Prestige = 5
                }, cancellationToken);
                break;

            default:
                return AuthServiceResult<object>.BadRequest($"Vai trò '{role}' không hợp lệ. Giá trị cho phép: Player, VenueOwner.");
        }

        await _userRepository.SaveChangesAsync(cancellationToken);

        return AuthServiceResult<object>.Success(new
        {
            message = $"Đã gán vai trò '{role}' thành công.",
            userType = user.UserType
        });
    }

    public async Task<AuthServiceResult<UserResponse>> GetMeAsync(int? userId, CancellationToken cancellationToken)
    {
        if (userId is null) return AuthServiceResult<UserResponse>.Unauthorized();

        var user = await _userRepository.GetByIdAsync(userId.Value, cancellationToken);
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
                    ? "Phiên đăng ký Google không hợp lệ. Vui lòng thử lại."
                    : "Phiên đăng nhập Google không hợp lệ. Vui lòng thử lại.");
        }
        catch (InvalidOperationException exception)
        {
            return AuthServiceResult<GoogleUserInfo>.Problem(
                "Cấu hình đăng nhập Google chưa hợp lệ.",
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

        while (await _userRepository.ExistsByUsernameAsync(candidate, cancellationToken))
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
