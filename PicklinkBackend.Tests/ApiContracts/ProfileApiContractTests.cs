namespace PicklinkBackend.Tests;

public class ProfileApiContractTests
{
    [Fact]
    public void ProfileControllerDelegatesProfileWorkflow()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Players", "ProfileController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Players", "PlayerProfileService.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));
        var schema = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

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
        Assert.Contains("COL_LENGTH(N'PLAYER', N'phoneNumber')", schema);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var fileName = cleanSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;

                var foundDir = Directory.GetDirectories(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundDir is not null) return foundDir;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
