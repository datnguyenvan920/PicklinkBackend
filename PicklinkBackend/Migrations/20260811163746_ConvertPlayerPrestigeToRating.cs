using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class ConvertPlayerPrestigeToRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "prestige",
                table: "PLAYER",
                type: "float",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.Sql("""
                UPDATE [player]
                SET [player].[prestige] = ROUND((5.0 + COALESCE([reviews].[scoreTotal], 0)) / (1.0 + COALESCE([reviews].[reviewCount], 0)), 1)
                FROM [PLAYER] AS [player]
                LEFT JOIN (
                    SELECT [revieweePlayerId], SUM([score]) AS [scoreTotal], COUNT(*) AS [reviewCount]
                    FROM [MATCH_PLAYER_REVIEW]
                    GROUP BY [revieweePlayerId]
                ) AS [reviews] ON [reviews].[revieweePlayerId] = [player].[playerId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [PLAYER] SET [prestige] = ROUND([prestige] * 20, 0);");
            migrationBuilder.AlterColumn<int>(
                name: "prestige",
                table: "PLAYER",
                type: "int",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");
        }
    }
}
