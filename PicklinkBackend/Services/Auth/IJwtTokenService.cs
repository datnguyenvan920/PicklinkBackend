using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Auth;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
