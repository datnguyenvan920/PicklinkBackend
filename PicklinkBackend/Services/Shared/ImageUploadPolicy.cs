using Microsoft.AspNetCore.Http;

namespace PicklinkBackend.Services.Shared;

public static class ImageUploadPolicy
{
    private const int HeaderLength = 12;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static async Task<bool> HasValidSignatureAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0) return false;

        var header = new byte[HeaderLength];
        await using var stream = file.OpenReadStream();
        var bytesRead = 0;
        while (bytesRead < header.Length)
        {
            var read = await stream.ReadAsync(header.AsMemory(bytesRead), cancellationToken);
            if (read == 0) break;
            bytesRead += read;
        }

        return MatchesDeclaredContentType(file.ContentType, header.AsSpan(0, bytesRead));
    }

    public static bool MatchesDeclaredContentType(string? contentType, ReadOnlySpan<byte> header)
    {
        var normalizedType = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalizedType switch
        {
            "image/jpeg" => IsJpeg(header),
            "image/png" => header.StartsWith(PngSignature),
            "image/webp" => IsWebp(header),
            "image/gif" => IsGif(header),
            _ => false
        };
    }

    private static bool IsJpeg(ReadOnlySpan<byte> header) =>
        header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

    private static bool IsWebp(ReadOnlySpan<byte> header) =>
        header.Length >= 12
        && header[..4].SequenceEqual("RIFF"u8)
        && header.Slice(8, 4).SequenceEqual("WEBP"u8);

    private static bool IsGif(ReadOnlySpan<byte> header) =>
        header.Length >= 6
        && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8));
}
