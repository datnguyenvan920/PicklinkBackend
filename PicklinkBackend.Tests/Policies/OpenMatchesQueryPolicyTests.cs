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
    public void OpenMatchesRepairsLegacyExpiredRoomsThatStillHaveMembers()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("Expired", source);
        Assert.Contains("match.MatchParticipants.Any", source);
    }

    [Fact]
    public void OpenMatchesIncludesFutureOpenReplacementSlots()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var openMatches = MethodBody(source, "GetOpenMatches", "GetMyOpenMatches");

        Assert.Contains("source, \"replacement\"", openMatches);
        Assert.Contains("match.SlotAbsences.Any(absence =>", openMatches);
        Assert.Contains("absence.Status == \"Open\"", openMatches);
        Assert.Contains("absence.BookingCheckInGroup.StartTime > VietnamTime.Now", openMatches);
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

    [Fact]
    public void MyMatchesHidesRoomsWithoutApprovedMembers()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("match.MatchParticipants.Any", source);
        Assert.Contains("Approved", source);
        Assert.Contains("Accepted", source);
    }

    [Fact]
    public void MatchRoomDoesNotWaitForPaymentReconciliationUnlessRequestedByCheckout()
    {
        var service = File.ReadAllText(MatchControllerSourcePath());
        var controller = File.ReadAllText(SourcePath("Controllers", "Matches", "MatchController.Open.cs"));

        Assert.Contains("bool reconcilePayments = false", controller);
        Assert.Contains("bool reconcilePayments = false)", service);
        Assert.Contains("if (reconcilePayments && await ReconcilePendingMatchPaymentsAsync", service);
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
        return SourcePath("Services", "Matches", "Implementations", "MatchService.cs");
    }

    private static string SourcePath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, "PicklinkBackend", .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {parts[^1]}.");
    }
}
