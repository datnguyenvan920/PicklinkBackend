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

    [Fact]
    public void MatchCheckInUsesThePaidPlayersExistingUniqueTransferCode()
    {
        var open = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var staff = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "StaffOperationsDtos.cs"));

        Assert.Contains("_matchRepository", open);
        Assert.Contains("OperationsBookingQuery", staff);
        Assert.Contains("public int? VerifiedPlayerId", dto);
    }

    [Fact]
    public void MarkReadyToBookPersistsTheReadyStateAndReturnsTheUpdatedRoom()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));
        var methodStart = source.IndexOf(" MarkReadyToBook(", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(" CreateMatchBooking(", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0 && methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("match.HostPlayerId != player.PlayerId", method);
        Assert.Contains("approvedCount < match.RequiredPlayerCount", method);
        Assert.Contains("match.Status = \"ReadyToBook\"", method);
        Assert.Contains("SaveChangesAsync", method);
        Assert.Contains("CommitAsync", method);
        Assert.Contains("_matchRealtime.Publish(matchId, \"ReadyToBook\")", method);
        Assert.Contains("LoadOpenMatchResponseAsync(matchId, player.PlayerId", method);
        Assert.DoesNotContain("new OpenMatchDetailResponse()", method);
    }

    [Fact]
    public void MatchDetailOnlyReturnsPersonalCheckInCodeAfterPaymentInsideTheCheckInWindow()
    {
        var detailSource = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var visibilitySource = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.ReplacementResponses.cs"));

        Assert.Contains("BuildVisibleBookingRoundsAsync(", detailSource);
        Assert.DoesNotContain("CheckInCode = g.CheckInCode", detailSource);
        Assert.Contains("payment.PayerId == currentPlayerId && payment.Status == \"Paid\"", visibilitySource);
        Assert.Contains("booking.Status == \"Confirmed\"", visibilitySource);
        Assert.Contains("localNow >= group.StartTime.AddMinutes(-30)", visibilitySource);
        Assert.Contains("playerPayment?.TransferCode", visibilitySource);
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
