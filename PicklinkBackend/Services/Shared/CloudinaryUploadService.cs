using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PicklinkBackend.Services.Shared;

public sealed class CloudinaryUploadService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CloudinaryUploadService> _logger;

    public CloudinaryUploadService(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<CloudinaryUploadService> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> UploadImageAsync(
        Stream imageStream,
        string fileName,
        string folder = "picklink_receipts",
        CancellationToken cancellationToken = default)
    {
        var cloudName = _configuration["Cloudinary:CloudName"];
        var apiKey = _configuration["Cloudinary:ApiKey"];
        var apiSecret = _configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrWhiteSpace(cloudName) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            _logger.LogError("Cloudinary credentials are not configured.");
            throw new InvalidOperationException("Máy chủ chưa cấu hình dịch vụ lưu trữ đám mây Cloudinary.");
        }

        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var stringToSign = $"folder={folder}&timestamp={timestamp}{apiSecret}";
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(stringToSign));
            var signature = Convert.ToHexString(hash).ToLowerInvariant();

            using var client = _httpClientFactory.CreateClient();
            using var formData = new MultipartFormDataContent();

            if (imageStream.CanSeek)
            {
                imageStream.Position = 0;
            }

            var streamContent = new StreamContent(imageStream);
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var mimeType = extension switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

            formData.Add(streamContent, "file", fileName);
            formData.Add(new StringContent(apiKey), "api_key");
            formData.Add(new StringContent(timestamp), "timestamp");
            formData.Add(new StringContent(folder), "folder");
            formData.Add(new StringContent(signature), "signature");

            var response = await client.PostAsync(
                $"https://api.cloudinary.com/v1_1/{cloudName}/image/upload",
                formData,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Cloudinary upload failed with status {Status}: {Body}", response.StatusCode, body);
                throw new InvalidOperationException("Tải ảnh lên máy chủ đám mây thất bại. Vui lòng thử lại.");
            }

            var result = JsonSerializer.Deserialize<CloudinaryUploadResponse>(body);
            var secureUrl = result?.SecureUrl ?? result?.Url;
            if (string.IsNullOrWhiteSpace(secureUrl))
            {
                _logger.LogError("Cloudinary upload returned empty URL. Response: {Body}", body);
                throw new InvalidOperationException("Không nhận được đường dẫn ảnh từ máy chủ đám mây.");
            }

            return secureUrl;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Exception while uploading image to Cloudinary.");
            throw new InvalidOperationException("Không thể kết nối đến máy chủ lưu trữ ảnh đám mây. Vui lòng thử lại.");
        }
    }

    private sealed class CloudinaryUploadResponse
    {
        [JsonPropertyName("secure_url")]
        public string? SecureUrl { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("public_id")]
        public string? PublicId { get; set; }
    }
}
