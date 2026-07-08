namespace PicklinkBackend.Tests;

public class MatchSlotVotingPolicyTests
{
    [Fact]
    public void MatchSlotVotingEndpointsRequireApprovedParticipantsAndScheduleConflictChecks()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("GetMatchSlotOptions", source);
        Assert.Contains("VoteMatchSlot", source);
        Assert.Contains("UnvoteMatchSlot", source);
        Assert.Contains("EnsureApprovedParticipantAsync", source);
        Assert.Contains("_playerScheduleConflict.HasConflictAsync", source);
        Assert.Contains("IsCompatibleForAll", source);
    }

    [Fact]
    public void MatchSlotVotesRequireReadyMatchAndPreferredVenue()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.True(CountOccurrences(source, "context.Value.Match.Status != \"ReadyToBook\"") >= 2);
        Assert.True(CountOccurrences(source, "PreferredVenueIds(context.Value.Match).Contains") >= 2);
    }

    [Fact]
    public void MatchSlotVoteModelIsUniquePerPlayerAndSlot()
    {
        var dbContext = File.ReadAllText(ApplicationDbContextSourcePath());
        var model = File.ReadAllText(MatchSlotVoteSourcePath());

        Assert.Contains("DbSet<MatchSlotVote>", dbContext);
        Assert.Contains("MATCH_SLOT_VOTE", dbContext);
        Assert.Contains("UQ_MATCH_SLOT_VOTE_player_slot", dbContext);
        Assert.Contains("public int MatchSlotVoteId", model);
        Assert.Contains("public int MatchId", model);
        Assert.Contains("public int PlayerId", model);
        Assert.Contains("public int CourtId", model);
        Assert.Contains("public DateTime StartTime", model);
        Assert.Contains("public DateTime EndTime", model);
    }

    [Fact]
    public void SlotOptionQueryEnsuresVoteTableExistsBeforeReadingVotes()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var builder = ExtractMethod(source, "private async Task<List<MatchSlotOptionResponse>> BuildMatchSlotOptionsAsync");

        Assert.Contains("await EnsureMatchSlotVoteSchemaAsync(cancellationToken)", builder);
        Assert.True(
            builder.IndexOf("await EnsureMatchSlotVoteSchemaAsync(cancellationToken)", StringComparison.Ordinal)
            < builder.IndexOf("_db.MatchSlotVotes.AsNoTracking()", StringComparison.Ordinal));
        Assert.Contains("IF OBJECT_ID(N'[MATCH_SLOT_VOTE]', N'U') IS NULL", source);
    }

    private static string MatchControllerSourcePath() => Locate("PicklinkBackend", "Services", "MatchService.Open.cs");

    private static string ApplicationDbContextSourcePath() => Locate("PicklinkBackend", "Data", "ApplicationDbContext.cs");

    private static string MatchSlotVoteSourcePath() => Locate("PicklinkBackend", "Models", "MatchSlotVote.cs");

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count += 1;
            index += value.Length;
        }

        return count;
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");

        var nextMethod = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return nextMethod < 0 ? source[start..] : source[start..nextMethod];
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

        throw new FileNotFoundException($"Could not locate {Path.Combine(parts)} from the test output directory.");
    }
}
