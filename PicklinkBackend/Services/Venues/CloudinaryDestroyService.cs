using System.Security.Cryptography;
using System.Text;

namespace PicklinkBackend.Services.Venues;

public sealed class CloudinaryDestroyService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public CloudinaryDestroyService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> DestroyAsync(string publicId, CancellationToken cancellationToken = default)
    {
        var cloudName = _configuration["Cloudinary:CloudName"];
        var apiKey = _configuration["Cloudinary:ApiKey"];
        var apiSecret = _configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret) || string.IsNullOrWhiteSpace(publicId))
        {
            return false;
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var stringToSign = $"public_id={publicId}&timestamp={timestamp}{apiSecret}";
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(stringToSign));
            var signature = Convert.ToHexString(hash).ToLowerInvariant();

            using var client = _httpClientFactory.CreateClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["public_id"] = publicId,
                ["timestamp"] = timestamp,
                ["api_key"] = apiKey,
                ["signature"] = signature
            });

            var response = await client.PostAsync($"https://api.cloudinary.com/v1_1/{cloudName}/image/destroy", content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryExtractPublicId(string? imageUrl, out string publicId)
    {
        publicId = string.Empty;
        if (string.IsNullOrWhiteSpace(imageUrl) || !imageUrl.Contains("cloudinary.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var uploadIndex = imageUrl.IndexOf("/upload/", StringComparison.OrdinalIgnoreCase);
        if (uploadIndex == -1) return false;

        var pathAfterUpload = imageUrl[(uploadIndex + "/upload/".Length)..];
        if (pathAfterUpload.StartsWith('v') && pathAfterUpload.Contains('/'))
        {
            var firstSlashIndex = pathAfterUpload.IndexOf('/');
            if (firstSlashIndex != -1 && pathAfterUpload[1..firstSlashIndex].All(char.IsDigit))
            {
                pathAfterUpload = pathAfterUpload[(firstSlashIndex + 1)..];
            }
        }

        var lastDotIndex = pathAfterUpload.LastIndexOf('.');
        if (lastDotIndex != -1)
        {
            pathAfterUpload = pathAfterUpload[..lastDotIndex];
        }

        if (!string.IsNullOrWhiteSpace(pathAfterUpload))
        {
            publicId = pathAfterUpload;
            return true;
        }

        return false;
    }
}
