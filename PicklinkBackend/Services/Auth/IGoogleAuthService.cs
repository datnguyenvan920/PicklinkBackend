namespace PicklinkBackend.Services.Auth;

public interface IGoogleAuthService
{
    Task<GoogleUserInfo> VerifyIdTokenAsync(string idToken, CancellationToken cancellationToken = default);
}
