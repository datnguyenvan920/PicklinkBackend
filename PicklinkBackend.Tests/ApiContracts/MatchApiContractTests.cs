namespace PicklinkBackend.Tests;

public class MatchApiContractTests
{
    [Fact]
    public void MatchControllerSupportsFrontendPluralMatchesRoute()
    {
        var root = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.cs"));
        var open = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.Open.cs"));
        var recommendations = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.Recommendations.cs"));

        Assert.Contains("[Route(\"api/matches\")]", root);
        Assert.DoesNotContain("[Route(\"api/[controller]\")]", root);
        Assert.Contains("[HttpGet(\"venues\")]", open);
        Assert.Contains("[HttpGet(\"open\")]", open);
        Assert.Contains("[HttpPost(\"open\")]", open);
        Assert.Contains("[HttpGet(\"player-recommendations\")]", recommendations);
        Assert.Contains("[HttpPost(\"{matchId:int}/invitations\")]", recommendations);
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
