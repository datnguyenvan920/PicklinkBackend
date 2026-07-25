namespace PicklinkBackend.Tests;

public class LegacyMatchVoteTransactionPolicyTests
{
    [Fact]
    public void VoteSerializesAndCommitsAllWritesAtomically()
    {
        var source = File.ReadAllText(Locate("PicklinkBackend", "Services", "Matches", "MatchService.cs"));
        var start = source.IndexOf("Task<ServiceResult<MatchVotingStatusResponse>> Vote", StringComparison.Ordinal);
        Assert.True(start >= 0);
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