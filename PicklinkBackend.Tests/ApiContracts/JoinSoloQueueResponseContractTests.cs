namespace PicklinkBackend.Tests;

public sealed class JoinSoloQueueResponseContractTests
{
    [Fact]
    public void JoinSoloQueueReturnsTheQueueCreatedByTheCurrentRequest()
    {
        var source = File.ReadAllText(SourcePath());
        var joinSoloQueue = MethodBody(source, "JoinSoloQueue", "JoinLobbyQueue");

        Assert.Contains(
            "GetQueueStatusForPlayer(player.PlayerId, queueItem.MatchmakingQueueId, cancellationToken)",
            joinSoloQueue);
        Assert.DoesNotContain("GetQueueStatus(cancellationToken)", joinSoloQueue);
    }

    private static string MethodBody(string source, string methodName, string nextMethodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        var end = source.IndexOf($" {nextMethodName}(", start + methodName.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string SourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var relativePath = Path.Combine(
                "PicklinkBackend",
                "Services",
                "Matches",
                "Implementations",
                "MatchmakingService.cs");
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;

            candidate = Path.Combine(directory.FullName, "PicklinkBackend", relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MatchmakingService.cs.");
    }
}
