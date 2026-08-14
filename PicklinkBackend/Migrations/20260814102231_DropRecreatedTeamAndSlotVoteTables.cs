using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class DropRecreatedTeamAndSlotVoteTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RemoveUnusedTournamentAndMarketplaceTables already dropped these, but they
            // reappeared on the shared database with a schema this repository never
            // defined (auto-named keys, [time] instead of [datetime], reversed column
            // order), so something outside this codebase recreates them. Dropping them
            // here keeps every environment converging on the same schema; the guards make
            // the migration safe on databases where they are already gone.
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[PLAYER_TEAM_ROSTER]', N'U') IS NOT NULL
                    DROP TABLE [PLAYER_TEAM_ROSTER];
                IF OBJECT_ID(N'[MATCH_SLOT_VOTE]', N'U') IS NOT NULL
                    DROP TABLE [MATCH_SLOT_VOTE];
                IF OBJECT_ID(N'[TEAM]', N'U') IS NOT NULL
                    DROP TABLE [TEAM];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: these tables are not part of the model, so there is no
            // schema to restore. RemoveUnusedTournamentAndMarketplaceTables.Down() still
            // holds the original definitions if they are ever needed again.
        }
    }
}
