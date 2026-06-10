using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
