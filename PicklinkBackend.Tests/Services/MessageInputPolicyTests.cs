using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Tests.Services;

public sealed class MessageInputPolicyTests
{
    [Fact]
    public void Validate_TrimsContentAndMediaUrl()
    {
        var result = MessageInputPolicy.Validate("  hello  ", "  /uploads/chat/photo.png  ");

        Assert.True(result.IsValid);
        Assert.Equal("hello", result.Content);
        Assert.Equal("/uploads/chat/photo.png", result.MediaUrl);
    }

    [Fact]
    public void Validate_RejectsEmptyMessage()
    {
        var result = MessageInputPolicy.Validate("  ", null);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsContentAndMediaUrlOverTheirLimits()
    {
        var longContent = MessageInputPolicy.Validate(
            new string('a', MessageInputPolicy.MaximumContentLength + 1));
        var longMediaUrl = MessageInputPolicy.Validate(
            null,
            "/" + new string('a', MessageInputPolicy.MaximumMediaUrlLength));

        Assert.False(longContent.IsValid);
        Assert.False(longMediaUrl.IsValid);
    }

    [Theory]
    [InlineData("/uploads/chat/photo.png")]
    [InlineData("https://cdn.example.com/photo.png")]
    [InlineData("http://localhost/photo.png")]
    public void Validate_AcceptsSupportedMediaUrls(string mediaUrl)
    {
        var result = MessageInputPolicy.Validate(null, mediaUrl);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("//evil.example/photo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("ftp://example.com/photo.png")]
    public void Validate_RejectsUnsafeMediaUrls(string mediaUrl)
    {
        var result = MessageInputPolicy.Validate(null, mediaUrl);

        Assert.False(result.IsValid);
    }
}
