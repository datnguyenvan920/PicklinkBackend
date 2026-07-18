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
        Assert.Contains("_playerScheduleConflict.LoadBusyPeriodsAsync", source);
        Assert.Contains("IsCompatibleForAll", source);
    }

    [Fact]
    public void MatchSlotVotesRequireBookableMatchAndPreferredVenue()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var quote = ((char)34).ToString();

        Assert.True(CountOccurrences(source, "!CanCreateBooking(context.Value.Match.Status)") >= 2);
        Assert.Contains("status is " + quote + "ReadyToBook" + quote
            + " or " + quote + "Booked" + quote, source);
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
    public void SlotOptionQueryUsesMigrationManagedVoteTableAndBulkConflictLookup()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var builder = ExtractMethod(
            source,
            "private async Task<List<MatchSlotOptionResponse>> BuildMatchSlotOptionsAsync");

        Assert.Contains("_db.MatchSlotVotes.AsNoTracking()", builder);
        Assert.Contains("_playerScheduleConflict.LoadBusyPeriodsAsync", builder);
        Assert.DoesNotContain("_playerScheduleConflict.HasConflictAsync", builder);
        Assert.DoesNotContain("EnsureMatchSlotVoteSchemaAsync", source);
        Assert.DoesNotContain("IF OBJECT_ID(N'[MATCH_SLOT_VOTE]', N'U') IS NULL", source);
    }

    private static string MatchControllerSourcePath() =>
        Locate("PicklinkBackend", "Services", "Matches", "MatchService.Open.cs");

    private static string ApplicationDbContextSourcePath() =>
        Locate("PicklinkBackend", "Data", "ApplicationDbContext.cs");

    private static string MatchSlotVoteSourcePath() =>
        Locate("PicklinkBackend", "Models", "MatchSlotVote.cs");

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
        Assert.True(start >= 0, "Could not find method signature: " + signature);

        var nextMethod = source.IndexOf(Environment.NewLine + "    private ",
            start + signature.Length, StringComparison.Ordinal);
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

        throw new FileNotFoundException(
            "Could not locate " + Path.Combine(parts) + " from the test output directory.");
    }
}
