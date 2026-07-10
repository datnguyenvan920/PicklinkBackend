using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Venues;

public sealed class LocalUploadService
{
    private const long MaxClubCoverBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public LocalUploadService(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<LocalUploadResult> SaveClubCoverAsync(IFormFile image, CancellationToken cancellationToken)
    {
        if (image.Length == 0)
            return LocalUploadResult.BadRequest("Please choose an image.");

        if (image.Length > MaxClubCoverBytes)
            return LocalUploadResult.BadRequest("Image must not exceed 5 MB.");

        if (!AllowedImageContentTypes.Contains(image.ContentType))
            return LocalUploadResult.BadRequest("Only JPG, PNG or WEBP images are supported.");

        var extension = image.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"club-cover-{Guid.NewGuid():N}{extension}";
        var webRootPath = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(webRootPath, "uploads", "group-covers");
        Directory.CreateDirectory(directory);

        await using var stream = File.Create(Path.Combine(directory, fileName));
        await image.CopyToAsync(stream, cancellationToken);

        return LocalUploadResult.Success(new LocalUploadResponse
        {
            Url = PublicUrl($"/uploads/group-covers/{fileName}")
        });
    }

    private string PublicUrl(string relativeUrl)
    {
        var publicBaseUrl = _configuration["PublicBaseUrl"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(publicBaseUrl) ? relativeUrl : $"{publicBaseUrl}{relativeUrl}";
    }
}

public sealed record LocalUploadResult(
    LocalUploadResultStatus Status,
    LocalUploadResponse? Value,
    string? ErrorMessage)
{
    public static LocalUploadResult Success(LocalUploadResponse value) =>
        new(LocalUploadResultStatus.Success, value, ErrorMessage: null);

    public static LocalUploadResult BadRequest(string errorMessage) =>
        new(LocalUploadResultStatus.BadRequest, Value: null, errorMessage);
}

public enum LocalUploadResultStatus
{
    Success,
    BadRequest
}
