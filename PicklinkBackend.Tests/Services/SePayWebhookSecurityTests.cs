using System.Security.Cryptography;
using System.Text;
using PicklinkBackend.Services.Payments;

namespace PicklinkBackend.Tests.Services;

public class SePayWebhookSecurityTests
{
    [Fact]
    public void Verify_AcceptsValidSignatureAndRejectsTamperingOrReplay()
    {
        const string secret = "test-secret";
        const string body = "{\"id\":92704,\"transferAmount\":500000}";
        var now = DateTimeOffset.FromUnixTimeSeconds(1_750_000_000);
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        var signature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";

        Assert.True(SePayWebhookSecurity.Verify(body, timestamp, signature, secret, now));
        Assert.False(SePayWebhookSecurity.Verify(body + " ", timestamp, signature, secret, now));
        Assert.False(SePayWebhookSecurity.Verify(body, timestamp, signature, secret, now.AddMinutes(6)));
    }
}
