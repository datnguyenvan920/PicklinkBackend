using System.Security.Cryptography;
using System.Text;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Venues;

public sealed class CloudinarySignatureService
{
    private readonly IConfiguration _configuration;

    public CloudinarySignatureService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public CloudinarySignatureResult Generate(SignatureRequest request)
    {
        var cloudName = _configuration["Cloudinary:CloudName"];
        var apiKey = _configuration["Cloudinary:ApiKey"];
        var apiSecret = _configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            return CloudinarySignatureResult.NotConfigured("Cloudinary is not configured on the server.");
        }

        if (!CloudinarySignaturePolicy.TryValidate(request.Parameters, out var parametersToSign))
        {
            return CloudinarySignatureResult.BadRequest("Unsupported Cloudinary signature parameters.");
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        parametersToSign["timestamp"] = timestamp;
        var filteredParams = parametersToSign
            .OrderBy(parameter => parameter.Key)
            .Select(parameter => $"{parameter.Key}={parameter.Value}");
        var stringToSign = string.Join("&", filteredParams) + apiSecret;
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(stringToSign));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();

        return CloudinarySignatureResult.Success(new SignatureResponse
        {
            Signature = signature,
            Timestamp = timestamp,
            ApiKey = apiKey,
            CloudName = cloudName
        });
    }
}

public sealed record CloudinarySignatureResult(
    CloudinarySignatureResultStatus Status,
    SignatureResponse? Signature,
    string? ErrorMessage)
{
    public static CloudinarySignatureResult Success(SignatureResponse signature) =>
        new(CloudinarySignatureResultStatus.Success, signature, ErrorMessage: null);

    public static CloudinarySignatureResult BadRequest(string errorMessage) =>
        new(CloudinarySignatureResultStatus.BadRequest, Signature: null, errorMessage);

    public static CloudinarySignatureResult NotConfigured(string errorMessage) =>
        new(CloudinarySignatureResultStatus.NotConfigured, Signature: null, errorMessage);
}

public enum CloudinarySignatureResultStatus
{
    Success,
    BadRequest,
    NotConfigured
}