using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
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

    [Fact]
    public void VerifyRequest_AcceptsApiKeyAuthorizationHeader()
    {
        const string secret = "picklink_sepay_webhook_secret_key";
        const string apiToken = "BKDPWIO1H01ZLGWYDYSOFAJ448F6IM5DYTNBU06ECRZQC5MZKSWDS2BKJVJTAMEP";
        const string body = "{\"id\":92704,\"transferAmount\":500000}";
        var now = DateTimeOffset.UtcNow;

        var headersWithApiKey = new HeaderDictionary
        {
            { "Authorization", new StringValues($"Apikey {apiToken}") }
        };
        Assert.True(SePayWebhookSecurity.VerifyRequest(body, headersWithApiKey, new QueryCollection(), secret, apiToken, now));

        var headersWithSecret = new HeaderDictionary
        {
            { "Authorization", new StringValues($"Apikey {secret}") }
        };
        Assert.True(SePayWebhookSecurity.VerifyRequest(body, headersWithSecret, new QueryCollection(), secret, apiToken, now));

        var headersWithBearer = new HeaderDictionary
        {
            { "Authorization", new StringValues($"Bearer {apiToken}") }
        };
        Assert.True(SePayWebhookSecurity.VerifyRequest(body, headersWithBearer, new QueryCollection(), secret, apiToken, now));

        var headersWithCustomHeader = new HeaderDictionary
        {
            { "X-API-KEY", new StringValues(secret) }
        };
        Assert.True(SePayWebhookSecurity.VerifyRequest(body, headersWithCustomHeader, new QueryCollection(), secret, apiToken, now));

        var invalidHeaders = new HeaderDictionary
        {
            { "Authorization", new StringValues("Apikey wrong-token") }
        };
        Assert.False(SePayWebhookSecurity.VerifyRequest(body, invalidHeaders, new QueryCollection(), secret, apiToken, now));
    }
}
