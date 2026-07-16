using System.Security.Cryptography;
using System.Text;

namespace PicklinkBackend.Services.Payments;

public static class SePayWebhookSecurity
{
    public static bool Verify(string rawBody, string? timestampHeader, string? signatureHeader, string secret, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secret)
            || !long.TryParse(timestampHeader, out var timestamp)
            || Math.Abs(now.ToUnixTimeSeconds() - timestamp) > 300
            || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        var payload = Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        var expected = Encoding.ASCII.GetBytes($"sha256={Convert.ToHexString(hash).ToLowerInvariant()}");
        var actual = Encoding.ASCII.GetBytes(signatureHeader.Trim());
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
