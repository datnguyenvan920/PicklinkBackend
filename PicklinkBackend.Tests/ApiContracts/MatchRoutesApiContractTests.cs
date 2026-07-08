namespace PicklinkBackend.Tests;

public class MatchRoutesApiContractTests
{
    [Fact]
    public void MatchControllerUsesReadablePluralRoutes()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.cs"));
        var lobby = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.Lobby.cs"));

        Assert.Contains("[Route(\"api/matches\")]", controller);
        Assert.DoesNotContain("[Route(\"api/[controller]\")]", controller);

        Assert.Contains("[HttpPost]", lobby);
        Assert.DoesNotContain("[HttpPost(\"matches\")]", lobby);
        Assert.Contains("[HttpGet(\"{matchId:int}/voting-status\")]", lobby);
        Assert.Contains("[HttpPost(\"{matchId:int}/vote\")]", lobby);
        Assert.Contains("[HttpGet(\"{matchId:int}/detail\")]", lobby);
        Assert.Contains("[HttpGet(\"{matchId:int}/messages\")]", lobby);
        Assert.Contains("[HttpPost(\"{matchId:int}/messages\")]", lobby);
    }

    [Fact]
    public void MatchPartialFilesFollowControllerDotFeatureNaming()
    {
        var matchesDirectory = SourceDirectory("Controllers", "Matches");

        Assert.True(File.Exists(Path.Combine(matchesDirectory, "MatchController.Open.cs")));
        Assert.True(File.Exists(Path.Combine(matchesDirectory, "MatchController.Lobby.cs")));
        Assert.True(File.Exists(Path.Combine(matchesDirectory, "MatchController.Recommendations.cs")));

        Assert.False(File.Exists(Path.Combine(matchesDirectory, "MatchOpenController.cs")));
        Assert.False(File.Exists(Path.Combine(matchesDirectory, "MatchLobbyController.cs")));
        Assert.False(File.Exists(Path.Combine(matchesDirectory, "MatchRecommendationsController.cs")));
    }

    [Fact]
    public void ControllersUseExplicitApiRoutes()
    {
        var controllersDirectory = SourceDirectory("Controllers");
        var controllerFiles = Directory.GetFiles(controllersDirectory, "*.cs", SearchOption.AllDirectories);

        foreach (var file in controllerFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("[Route(\"api/[controller]\")]", source);
        }
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

    private static string SourceDirectory(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
