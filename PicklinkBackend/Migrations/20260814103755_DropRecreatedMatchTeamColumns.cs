using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class DropRecreatedMatchTeamColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Companion to DropRecreatedTeamAndSlotVoteTables: whatever recreates the TEAM
            // tables on the shared database also re-adds these columns to MATCH, and that
            // migration only dropped the tables. The columns are nullable with no data and
            // are no longer mapped, so EF ignores them, but they still leave the shared
            // database out of step with a freshly migrated one. Guards keep this safe to
            // run where the columns are already gone; the default constraints have to go
            // first because a column cannot be dropped while one references it.
            migrationBuilder.Sql("""
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql = @sql + N'ALTER TABLE [MATCH] DROP CONSTRAINT [' + d.name + N'];'
                FROM sys.default_constraints d
                JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
                WHERE d.parent_object_id = OBJECT_ID(N'[MATCH]')
                  AND c.name IN (N'team1Id', N'team2Id', N'winningTeamId');
                IF @sql <> N'' EXEC sp_executesql @sql;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH(N'MATCH', N'team1Id') IS NOT NULL
                    ALTER TABLE [MATCH] DROP COLUMN [team1Id];
                IF COL_LENGTH(N'MATCH', N'team2Id') IS NOT NULL
                    ALTER TABLE [MATCH] DROP COLUMN [team2Id];
                IF COL_LENGTH(N'MATCH', N'winningTeamId') IS NOT NULL
                    ALTER TABLE [MATCH] DROP COLUMN [winningTeamId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: these columns are not part of the model, so there is no
            // schema to restore. RemoveUnusedTournamentAndMarketplaceTables.Down() still
            // recreates them together with the TEAM table they referenced.
        }
    }
}
