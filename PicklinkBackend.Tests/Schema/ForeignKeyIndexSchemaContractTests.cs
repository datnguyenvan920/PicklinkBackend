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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidates = new[]
            {
                Path.Combine(new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray()),
                Path.Combine(new[] { directory.FullName, "PicklinkBackend", "PicklinkBackend" }.Concat(relativeSegments).ToArray())
            };
            var candidate = candidates.FirstOrDefault(File.Exists);
            if (candidate is not null) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate schema source file.");
    }
}