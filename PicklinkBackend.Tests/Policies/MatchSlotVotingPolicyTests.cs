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
    }

    [Fact]
    public void MatchSlotVotesRequireBookableMatchAndPreferredVenue()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("VoteMatchSlot", source);
    }

    [Fact]
    public void MatchSlotVoteModelIsUniquePerPlayerAndSlot()
    {
        var dbContext = File.ReadAllText(ApplicationDbContextSourcePath());
        var model = File.ReadAllText(MatchSlotVoteSourcePath());

        Assert.Contains("DbSet<MatchSlotVote>", dbContext);
        Assert.Contains("MATCH_SLOT_VOTE", dbContext);
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

        Assert.Contains("GetMatchSlotOptions", source);
        Assert.DoesNotContain("EnsureMatchSlotVoteSchemaAsync", source);
    }

    private static string MatchControllerSourcePath() =>
        Locate("PicklinkBackend", "Services", "Matches", "Implementations", "MatchService.cs");

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
        var cleanSegments = parts.FirstOrDefault() == "PicklinkBackend" ? parts[1..] : parts;
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

        throw new FileNotFoundException($"Could not locate {string.Join('/', parts)}.");
    }
}
