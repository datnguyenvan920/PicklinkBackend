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
