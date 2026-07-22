using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Tests;

public class CloudinarySignaturePolicyTests
{
    [Theory]
    [InlineData("picklink_clubs")]
    [InlineData("picklink_avatars")]
    [InlineData("picklink_posts")]
    [InlineData("picklink_messages")]
    public void TryValidateAcceptsOnlyConfiguredUploadFolders(string folder)
    {
        var parameters = new Dictionary<string, string>
        {
            ["folder"] = folder
        };

        var isValid = CloudinarySignaturePolicy.TryValidate(parameters, out var validated);

        Assert.True(isValid);
        Assert.Equal(new Dictionary<string, string>
        {
            ["folder"] = folder
        }, validated);
    }

    [Fact]
    public void TryValidateRejectsDestroyOrArbitraryPublicIdParameters()
    {
        var parameters = new Dictionary<string, string>
        {
            ["folder"] = "picklink_clubs",
            ["public_id"] = "someone-elses-image"
        };

        var isValid = CloudinarySignaturePolicy.TryValidate(parameters, out var validated);

        Assert.False(isValid);
        Assert.Empty(validated);
    }

    [Fact]
    public void TryValidateRejectsMissingParameters()
    {
        var isValid = CloudinarySignaturePolicy.TryValidate(null, out var validated);

        Assert.False(isValid);
        Assert.Empty(validated);
    }
}
