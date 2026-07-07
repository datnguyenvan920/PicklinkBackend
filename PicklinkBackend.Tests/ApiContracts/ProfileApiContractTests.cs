namespace PicklinkBackend.Tests;

public class ProfileApiContractTests
{
    [Fact]
    public void ProfileControllerDelegatesProfileWorkflow()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Players", "ProfileController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "PlayerProfileService.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[HttpGet(\"me\")]", source);
        Assert.Contains("[HttpPost(\"me/avatar\")]", source);
        Assert.Contains("[HttpPut(\"me\")]", source);
        Assert.Contains("PlayerProfileService", source);
        Assert.Contains("services.AddScoped<PlayerProfileService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("IWebHostEnvironment", source);
        Assert.Contains("MaxAvatarBytes", service);
        Assert.Contains("AllowedAvatarExtensions", service);
        Assert.Contains("BuildProfileResponseAsync", service);
        Assert.Contains("ProfileImageUrl", service);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}