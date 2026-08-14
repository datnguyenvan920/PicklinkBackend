using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

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
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var expectedWithPrefix = Encoding.ASCII.GetBytes($"sha256={hex}");
        var expectedRaw = Encoding.ASCII.GetBytes(hex);
        var actual = Encoding.ASCII.GetBytes(signatureHeader.Trim().ToLowerInvariant());

        return (expectedWithPrefix.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expectedWithPrefix, actual))
            || (expectedRaw.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expectedRaw, actual));
    }

    public static bool VerifyApiKey(string? incomingKey, string expectedKey)
    {
        if (string.IsNullOrWhiteSpace(incomingKey) || string.IsNullOrWhiteSpace(expectedKey))
            return false;

        var cleanedIncoming = incomingKey.Trim();
        if (cleanedIncoming.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase))
            cleanedIncoming = cleanedIncoming[7..].Trim();
        else if (cleanedIncoming.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            cleanedIncoming = cleanedIncoming[7..].Trim();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey.Trim());
        var actualBytes = Encoding.UTF8.GetBytes(cleanedIncoming);
        return expectedBytes.Length == actualBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static bool VerifyRequest(
        string rawBody,
        IHeaderDictionary headers,
        IQueryCollection query,
        string? configuredSecret,
        string? configuredApiToken,
        DateTimeOffset now)
    {
        var secrets = new[] { configuredSecret, configuredApiToken }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct()
            .ToArray();

        if (secrets.Length == 0) return false;

        // 1. Check HMAC signature if timestamp and signature headers are present
        var timestamp = headers["X-SePay-Timestamp"].FirstOrDefault()
            ?? headers["X-Timestamp"].FirstOrDefault()
            ?? headers["Timestamp"].FirstOrDefault();

        var signature = headers["X-SePay-Signature"].FirstOrDefault()
            ?? headers["X-Signature"].FirstOrDefault()
            ?? headers["Signature"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(timestamp) && !string.IsNullOrWhiteSpace(signature))
        {
            foreach (var sec in secrets)
            {
                if (Verify(rawBody, timestamp, signature, sec, now))
                    return true;
            }
        }

        // 2. Check API Key / Authorization header
        var authHeader = headers["Authorization"].FirstOrDefault()
            ?? headers["X-API-KEY"].FirstOrDefault()
            ?? headers["X-SePay-API-Key"].FirstOrDefault()
            ?? headers["ApiKey"].FirstOrDefault()
            ?? query["api_key"].FirstOrDefault()
            ?? query["secret"].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            foreach (var sec in secrets)
            {
                if (VerifyApiKey(authHeader, sec))
                    return true;
            }
        }

        return false;
    }
}
