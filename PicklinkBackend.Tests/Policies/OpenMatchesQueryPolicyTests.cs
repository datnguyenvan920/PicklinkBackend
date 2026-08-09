namespace PicklinkBackend.Tests;

public class OpenMatchesQueryPolicyTests
{
    [Fact]
    public void OpenMatchesUsesALeanSearchQueryInsteadOfDetailIncludes()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("GetOpenMatches", source);
    }

    [Fact]
    public void MyMatchesAvoidsCollectionsThatAreNotRenderedByTheList()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("MyMatches", source);
    }

    [Fact]
    public void OpenMatchesExcludesTheCurrentPlayersOwnedAndJoinedRoomsBeforePagination()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        var openMatches = MethodBody(source, "GetOpenMatches", "GetMyOpenMatches");

        Assert.Contains("match.HostPlayerId != playerId", openMatches);
        Assert.Contains("participant.PlayerId == playerId", openMatches);
        Assert.Contains("participant.Status == \"Approved\"", openMatches);
        Assert.DoesNotContain("participant.Status == \"Invited\"", openMatches);
        Assert.True(
            openMatches.IndexOf("match.HostPlayerId != playerId", StringComparison.Ordinal) <
            openMatches.IndexOf("query.CountAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenMatchesExcludesFullRoomsBeforePagination()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var openMatches = MethodBody(source, "GetOpenMatches", "GetMyOpenMatches");

        Assert.Contains("match.Status == \"Recruiting\"", openMatches);
        Assert.Contains("participant.Status == \"Approved\" || participant.Status == \"Accepted\"", openMatches);
        Assert.Contains("< match.RequiredPlayerCount", openMatches);
        Assert.DoesNotContain("match.Status == \"ReadyToBook\"", openMatches);
        Assert.True(
            openMatches.IndexOf("< match.RequiredPlayerCount", StringComparison.Ordinal) <
            openMatches.IndexOf("query.CountAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void MyMatchesContainsOnlyOwnedRequestedOrJoinedRooms()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var myMatches = MethodBody(source, "GetMyOpenMatches", "GetOpenMatchDetail");

        Assert.Contains("match.HostPlayerId == player.PlayerId", myMatches);
        Assert.Contains("participant.Status == \"Pending\"", myMatches);
        Assert.Contains("participant.Status == \"Approved\"", myMatches);
        Assert.Contains("participant.Status == \"Accepted\"", myMatches);
        Assert.DoesNotContain("participant.Status == \"Invited\"", myMatches);
        Assert.DoesNotContain("participant.Status != \"Rejected\"", myMatches);
    }

    private static string MethodBody(string source, string methodName, string nextMethodName)
    {
        var start = source.IndexOf($" {methodName}(", StringComparison.Ordinal);
        var end = source.IndexOf($" {nextMethodName}(", start + methodName.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }

    private static string MatchControllerSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend", "Services", "Matches", "Implementations", "MatchService.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MatchService.cs.");
    }
}
