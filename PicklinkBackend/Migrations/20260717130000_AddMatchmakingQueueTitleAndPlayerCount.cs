using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717130000_AddMatchmakingQueueTitleAndPlayerCount")]
public partial class AddMatchmakingQueueTitleAndPlayerCount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "title",
            table: "MATCHMAKING_QUEUE",
            type: "nvarchar(150)",
            maxLength: 150,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "playerCount",
            table: "MATCHMAKING_QUEUE",
            type: "int",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE [MATCHMAKING_QUEUE]
            SET [title] = N'Lời mời ghép trận',
                [playerCount] = CASE WHEN [matchType] = '1vs1' THEN 2 ELSE 4 END
            WHERE [title] IS NULL OR [playerCount] IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "title",
            table: "MATCHMAKING_QUEUE",
            type: "nvarchar(150)",
            maxLength: 150,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(150)",
            oldMaxLength: 150,
            oldNullable: true);

        migrationBuilder.AlterColumn<int>(
            name: "playerCount",
            table: "MATCHMAKING_QUEUE",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "title", table: "MATCHMAKING_QUEUE");
        migrationBuilder.DropColumn(name: "playerCount", table: "MATCHMAKING_QUEUE");
    }
}
