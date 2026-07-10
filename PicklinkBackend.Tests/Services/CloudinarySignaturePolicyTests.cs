using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Tests;

public class CloudinarySignaturePolicyTests
{
    [Fact]
    public void TryValidateAcceptsOnlyTheConfiguredUploadFolder()
    {
        var parameters = new Dictionary<string, string>
        {
            ["folder"] = "picklink_clubs"
        };

        var isValid = CloudinarySignaturePolicy.TryValidate(parameters, out var validated);

        Assert.True(isValid);
        Assert.Equal(new Dictionary<string, string>
        {
            ["folder"] = "picklink_clubs"
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
