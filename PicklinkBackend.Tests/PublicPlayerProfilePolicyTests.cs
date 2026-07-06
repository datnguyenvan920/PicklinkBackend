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
        var controllerSource = File.ReadAllText(SourcePath("Controllers", "ProfileController.cs"));

        Assert.Contains("[AllowAnonymous]", controllerSource);
        Assert.Contains("[HttpGet(\"players/{playerId:int}\")]", controllerSource);
        Assert.Contains("GetPublicPlayerProfile", controllerSource);
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

    private static string ExtractClass(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find class signature: {signature}");

        var nextClass = source.IndexOf("\npublic class ", start + signature.Length, StringComparison.Ordinal);
        return nextClass < 0 ? source[start..] : source[start..nextClass];
    }
}
