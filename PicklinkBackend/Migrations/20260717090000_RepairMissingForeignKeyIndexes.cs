using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717090000_RepairMissingForeignKeyIndexes")]
public partial class RepairMissingForeignKeyIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_CONVERSATION_matchId' AND [object_id] = OBJECT_ID(N'[CONVERSATION]'))
                CREATE INDEX [IX_CONVERSATION_matchId] ON [CONVERSATION] ([matchId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_LISTING_FEE_SETTING_updatedByUserId' AND [object_id] = OBJECT_ID(N'[LISTING_FEE_SETTING]'))
                CREATE INDEX [IX_LISTING_FEE_SETTING_updatedByUserId] ON [LISTING_FEE_SETTING] ([updatedByUserId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_MATCH_hostPlayerId' AND [object_id] = OBJECT_ID(N'[MATCH]'))
                CREATE INDEX [IX_MATCH_hostPlayerId] ON [MATCH] ([hostPlayerId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_MATCH_PLAYER_REVIEW_reviewerPlayerId' AND [object_id] = OBJECT_ID(N'[MATCH_PLAYER_REVIEW]'))
                CREATE INDEX [IX_MATCH_PLAYER_REVIEW_reviewerPlayerId] ON [MATCH_PLAYER_REVIEW] ([reviewerPlayerId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_MATCH_SLOT_VOTE_playerId' AND [object_id] = OBJECT_ID(N'[MATCH_SLOT_VOTE]'))
                CREATE INDEX [IX_MATCH_SLOT_VOTE_playerId] ON [MATCH_SLOT_VOTE] ([playerId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_POST_COMMENT_LIKE_userId' AND [object_id] = OBJECT_ID(N'[POST_COMMENT_LIKE]'))
                CREATE INDEX [IX_POST_COMMENT_LIKE_userId] ON [POST_COMMENT_LIKE] ([userId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_VENUE_LISTING_PAYMENT_reviewedByUserId' AND [object_id] = OBJECT_ID(N'[VENUE_LISTING_PAYMENT]'))
                CREATE INDEX [IX_VENUE_LISTING_PAYMENT_reviewedByUserId] ON [VENUE_LISTING_PAYMENT] ([reviewedByUserId]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS [IX_CONVERSATION_matchId] ON [CONVERSATION];
            DROP INDEX IF EXISTS [IX_LISTING_FEE_SETTING_updatedByUserId] ON [LISTING_FEE_SETTING];
            DROP INDEX IF EXISTS [IX_MATCH_hostPlayerId] ON [MATCH];
            DROP INDEX IF EXISTS [IX_MATCH_PLAYER_REVIEW_reviewerPlayerId] ON [MATCH_PLAYER_REVIEW];
            DROP INDEX IF EXISTS [IX_MATCH_SLOT_VOTE_playerId] ON [MATCH_SLOT_VOTE];
            DROP INDEX IF EXISTS [IX_POST_COMMENT_LIKE_userId] ON [POST_COMMENT_LIKE];
            DROP INDEX IF EXISTS [IX_VENUE_LISTING_PAYMENT_reviewedByUserId] ON [VENUE_LISTING_PAYMENT];
            """);
    }
}