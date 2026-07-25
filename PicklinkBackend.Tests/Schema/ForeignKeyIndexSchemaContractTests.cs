namespace PicklinkBackend.Tests;

public class ForeignKeyIndexSchemaContractTests
{
    [Fact]
    public void StartupAndMigrationRepairAllKnownMissingForeignKeyIndexes()
    {
        var startup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));
        var migration = File.ReadAllText(SourcePath("Migrations", "20260717090000_RepairMissingForeignKeyIndexes.cs"));
        string[] expectedIndexes =
        [
            "IX_CONVERSATION_matchId",
            "IX_LISTING_FEE_SETTING_updatedByUserId",
            "IX_MATCH_hostPlayerId",
            "IX_MATCH_PLAYER_REVIEW_reviewerPlayerId",
            "IX_MATCH_SLOT_VOTE_playerId",
            "IX_POST_COMMENT_LIKE_userId",
            "IX_VENUE_LISTING_PAYMENT_reviewedByUserId"
        ];

        foreach (var index in expectedIndexes)
        {
            Assert.Contains(index, startup);
            Assert.Contains(index, migration);
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
}