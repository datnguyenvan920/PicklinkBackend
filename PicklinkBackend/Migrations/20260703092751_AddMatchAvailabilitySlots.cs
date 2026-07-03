using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchAvailabilitySlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [MATCH_AVAILABILITY_SLOT] (
                        [matchAvailabilitySlotId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_AVAILABILITY_SLOT] PRIMARY KEY,
                        [matchId] int NOT NULL,
                        [timeStart] time NOT NULL,
                        [timeEnd] time NOT NULL,
                        CONSTRAINT [FK_MATCH_AVAILABILITY_SLOT_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                        CONSTRAINT [CK_MATCH_AVAILABILITY_SLOT_time] CHECK ([timeEnd] > [timeStart])
                    );
                    CREATE INDEX [IX_MATCH_AVAILABILITY_SLOT_matchId]
                        ON [MATCH_AVAILABILITY_SLOT] ([matchId]);
                    CREATE UNIQUE INDEX [UQ_MATCH_AVAILABILITY_SLOT]
                        ON [MATCH_AVAILABILITY_SLOT] ([matchId], [timeStart], [timeEnd]);
                END
                IF COL_LENGTH(N'MATCH_AVAILABILITY_SLOT', N'availableDate') IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_AVAILABILITY_SLOT' AND object_id = OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]'))
                        DROP INDEX [UQ_MATCH_AVAILABILITY_SLOT] ON [MATCH_AVAILABILITY_SLOT];
                    ALTER TABLE [MATCH_AVAILABILITY_SLOT] DROP COLUMN [availableDate];
                END
                ;WITH [DuplicateSlots] AS (
                    SELECT [matchAvailabilitySlotId],
                        ROW_NUMBER() OVER (
                            PARTITION BY [matchId], [timeStart], [timeEnd]
                            ORDER BY [matchAvailabilitySlotId]
                        ) AS [rowNumber]
                    FROM [MATCH_AVAILABILITY_SLOT]
                )
                DELETE FROM [MATCH_AVAILABILITY_SLOT]
                WHERE [matchAvailabilitySlotId] IN (
                    SELECT [matchAvailabilitySlotId] FROM [DuplicateSlots] WHERE [rowNumber] > 1
                );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_MATCH_AVAILABILITY_SLOT' AND object_id = OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]'))
                    CREATE UNIQUE INDEX [UQ_MATCH_AVAILABILITY_SLOT]
                        ON [MATCH_AVAILABILITY_SLOT] ([matchId], [timeStart], [timeEnd]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[MATCH_AVAILABILITY_SLOT]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [MATCH_AVAILABILITY_SLOT];
                END
                """);
        }
    }
}
