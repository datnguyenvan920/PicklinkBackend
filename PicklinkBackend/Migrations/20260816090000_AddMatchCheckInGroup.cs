using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <summary>
    /// Attendance for a match booking used to be one row per player per match, which let a player
    /// who turned up for the first round count as present for every later one. It is now recorded
    /// against the check-in code that was scanned: one booking round, one court, adjacent slots.
    /// </summary>
    public partial class AddMatchCheckInGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'MATCH_CHECKIN', N'bookingCheckInGroupId') IS NULL
                    ALTER TABLE [MATCH_CHECKIN] ADD [bookingCheckInGroupId] int NULL;
                """);

            migrationBuilder.Sql(MatchCheckInGroupSql.Backfill);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MATCH_CHECKIN_GROUP')
                    ALTER TABLE [MATCH_CHECKIN] ADD CONSTRAINT [FK_MATCH_CHECKIN_GROUP]
                    FOREIGN KEY ([bookingCheckInGroupId]) REFERENCES [BOOKING_CHECKIN_GROUP]([bookingCheckInGroupId]);
                IF EXISTS (
                    SELECT 1 FROM sys.indexes i
                    WHERE i.name = N'UQ_MATCH_CHECKIN_UNIQUE'
                        AND i.object_id = OBJECT_ID(N'[MATCH_CHECKIN]')
                        AND (SELECT COUNT(*) FROM sys.index_columns ic
                            WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) = 2)
                    DROP INDEX [UQ_MATCH_CHECKIN_UNIQUE] ON [MATCH_CHECKIN];
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_CHECKIN_UNIQUE'
                    AND object_id = OBJECT_ID(N'[MATCH_CHECKIN]'))
                    CREATE UNIQUE INDEX [UQ_MATCH_CHECKIN_UNIQUE]
                    ON [MATCH_CHECKIN] ([matchId], [playerId], [bookingCheckInGroupId])
                    WHERE [bookingCheckInGroupId] IS NOT NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MATCH_CHECKIN_bookingCheckInGroupId'
                    AND object_id = OBJECT_ID(N'[MATCH_CHECKIN]'))
                    CREATE INDEX [IX_MATCH_CHECKIN_bookingCheckInGroupId]
                    ON [MATCH_CHECKIN] ([bookingCheckInGroupId]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Collapsing back to one row per player per match drops the extra rounds' attendance.
            migrationBuilder.Sql("""
                DELETE mc
                FROM [MATCH_CHECKIN] mc
                WHERE EXISTS (
                    SELECT 1 FROM [MATCH_CHECKIN] keep
                    WHERE keep.[matchId] = mc.[matchId]
                        AND keep.[playerId] = mc.[playerId]
                        AND keep.[checkinId] < mc.[checkinId]);

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MATCH_CHECKIN_bookingCheckInGroupId'
                    AND object_id = OBJECT_ID(N'[MATCH_CHECKIN]'))
                    DROP INDEX [IX_MATCH_CHECKIN_bookingCheckInGroupId] ON [MATCH_CHECKIN];
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_CHECKIN_UNIQUE'
                    AND object_id = OBJECT_ID(N'[MATCH_CHECKIN]'))
                    DROP INDEX [UQ_MATCH_CHECKIN_UNIQUE] ON [MATCH_CHECKIN];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_MATCH_CHECKIN_GROUP')
                    ALTER TABLE [MATCH_CHECKIN] DROP CONSTRAINT [FK_MATCH_CHECKIN_GROUP];
                IF COL_LENGTH(N'MATCH_CHECKIN', N'bookingCheckInGroupId') IS NOT NULL
                    ALTER TABLE [MATCH_CHECKIN] DROP COLUMN [bookingCheckInGroupId];
                CREATE UNIQUE INDEX [UQ_MATCH_CHECKIN_UNIQUE] ON [MATCH_CHECKIN] ([matchId], [playerId]);
                """);
        }
    }
}
