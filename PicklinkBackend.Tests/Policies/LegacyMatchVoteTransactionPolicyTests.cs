namespace PicklinkBackend.Tests;

public class LegacyMatchVoteTransactionPolicyTests
{
    [Fact]
    public void VoteSerializesAndCommitsAllWritesAtomically()
    {
        var source = File.ReadAllText(Locate("PicklinkBackend", "Services", "Matches", "MatchService.cs"));
        var start = source.IndexOf(
            "public async Task<ServiceResult<MatchVotingStatusResponse>> Vote(",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private async Task<MatchVotingStatusResponse> BuildVotingStatusResponse",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        var method = source[start..end];
        var transaction = method.IndexOf("BeginTransactionAsync(", StringComparison.Ordinal);
        var lockAcquisition = method.IndexOf("legacy-match-vote:{matchId}", StringComparison.Ordinal);
        var matchLoad = method.IndexOf("var match = await _db.Matches", StringComparison.Ordinal);
        var commit = method.IndexOf("transaction.CommitAsync(cancellationToken)", StringComparison.Ordinal);
        var finalSave = method.LastIndexOf("_db.SaveChangesAsync(cancellationToken)", StringComparison.Ordinal);

        Assert.Contains("IsolationLevel.Serializable", method);
        Assert.Contains("court-booking:{candidate.CourtId}", method);
        Assert.Contains("player-schedule:{matchParticipant.PlayerId}", method);
        Assert.Contains("_playerScheduleConflict.HasConflictAsync", method);
        Assert.Contains("_db.Bookings.AnyAsync", method);
        Assert.Contains("HourlyPriceSnapshot = selectedHourlyPrice", method);
        Assert.True(transaction >= 0 && transaction < lockAcquisition);
        Assert.True(lockAcquisition < matchLoad);
        Assert.True(finalSave >= 0 && finalSave < commit);
    }

    private static string Locate(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(parts)}.");
    }
}