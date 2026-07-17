using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717140000_AddMatchmakingQueueSkillRange")]
public partial class AddMatchmakingQueueSkillRange : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "minSkillLevel",
            table: "MATCHMAKING_QUEUE",
            type: "int",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "maxSkillLevel",
            table: "MATCHMAKING_QUEUE",
            type: "int",
            nullable: false,
            defaultValue: 5);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "minSkillLevel", table: "MATCHMAKING_QUEUE");
        migrationBuilder.DropColumn(name: "maxSkillLevel", table: "MATCHMAKING_QUEUE");
    }
}
