using Microsoft.AspNetCore.Http;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Tests.Services;

public sealed class ImageUploadPolicyTests
{
    public static TheoryData<string, byte[]> ValidImages => new()
    {
        { "image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10] },
        { "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A] },
        { "image/webp", [0x52, 0x49, 0x46, 0x46, 0x04, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50] },
        { "image/gif", [0x47, 0x49, 0x46, 0x38, 0x39, 0x61] }
    };

    [Theory]
    [MemberData(nameof(ValidImages))]
    public async Task HasValidSignatureAsync_AcceptsMatchingImage(string contentType, byte[] bytes)
    {
        var file = CreateFile(contentType, bytes);

        Assert.True(await ImageUploadPolicy.HasValidSignatureAsync(file));
    }

    [Fact]
    public async Task HasValidSignatureAsync_RejectsSpoofedImage()
    {
        var file = CreateFile("image/png", "this is not a png"u8.ToArray());

        Assert.False(await ImageUploadPolicy.HasValidSignatureAsync(file));
    }

    [Fact]
    public async Task HasValidSignatureAsync_RejectsContentTypeMismatch()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var file = CreateFile("image/png", jpegBytes);

        Assert.False(await ImageUploadPolicy.HasValidSignatureAsync(file));
    }

    private static FormFile CreateFile(string contentType, byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "image")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
