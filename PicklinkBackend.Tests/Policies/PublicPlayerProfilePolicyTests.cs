namespace PicklinkBackend.Tests;

public class PublicPlayerProfilePolicyTests
{
    [Fact]
    public void PublicPlayerProfileExposesOnlyBasicPlayingInformation()
    {
        var dtoSource = File.ReadAllText(SourcePath("DTOs", "UserProfileResponse.cs"));
        var profileDto = ExtractClass(dtoSource, "public class PublicPlayerProfileResponse");

        Assert.Contains("Username", profileDto);
        Assert.Contains("ProfileImageUrl", profileDto);
        Assert.Contains("SkillLevel", profileDto);
        Assert.Contains("Prestige", profileDto);
        Assert.Contains("MatchesPlayed", profileDto);
        Assert.DoesNotContain("Email", profileDto);
        Assert.DoesNotContain("BirthDate", profileDto);
        Assert.DoesNotContain("HeightCm", profileDto);
        Assert.DoesNotContain("WeightKg", profileDto);
    }

    [Fact]
public void PublicPlayerProfileCanBeViewedWithoutAuthentication()
    {
        var controllerSource = File.ReadAllText(SourcePath("Controllers", "Players", "ProfileController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Players", "PlayerProfileService.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[AllowAnonymous]", controllerSource);
        Assert.Contains("[HttpGet(\"players/{playerId:int}\")]", controllerSource);
        Assert.Contains("GetPublicPlayerProfile", controllerSource);
        Assert.Contains("PlayerProfileService", controllerSource);
        Assert.Contains("services.AddScoped<PlayerProfileService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", controllerSource);
        Assert.Contains("PublicPlayerProfileResponse", service);
        Assert.Contains("MatchParticipants.Count", service);
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

    private static string ExtractClass(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find class signature: {signature}");

        var nextClass = source.IndexOf("\npublic class ", start + signature.Length, StringComparison.Ordinal);
        return nextClass < 0 ? source[start..] : source[start..nextClass];
    }
}
