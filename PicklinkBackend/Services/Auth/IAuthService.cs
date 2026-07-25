using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Auth;

public interface IAuthService
{
    Task<AuthServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<AuthResponse>> GoogleLoginAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<AuthResponse>> GoogleRegisterAsync(GoogleLoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<ForgotPasswordResponse>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<object>> VerifyResetCodeAsync(VerifyPasswordResetCodeRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<object>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<object>> GetRoleStatusAsync(int? userId, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<object>> AssignRoleAsync(int? userId, AssignRoleRequest request, CancellationToken cancellationToken = default);
    Task<AuthServiceResult<UserResponse>> GetMeAsync(int? userId, CancellationToken cancellationToken = default);
}
