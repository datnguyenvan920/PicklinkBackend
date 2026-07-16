using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchmakingQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MATCHMAKING_QUEUE",
                columns: table => new
                {
                    matchmakingQueueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    playerId = table.Column<int>(type: "int", nullable: true),
                    matchId = table.Column<int>(type: "int", nullable: true),
                    matchType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    skillLevel = table.Column<int>(type: "int", nullable: false),
                    searchLatitude = table.Column<double>(type: "float", nullable: true),
                    searchLongitude = table.Column<double>(type: "float", nullable: true),
                    searchRadiusKm = table.Column<double>(type: "float", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCHMAKING_QUEUE", x => x.matchmakingQueueId);
                    table.ForeignKey(
                        name: "FK_MATCHMAKING_QUEUE_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MATCHMAKING_QUEUE_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MATCHMAKING_QUEUE_SLOT",
                columns: table => new
                {
                    matchmakingQueueSlotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchmakingQueueId = table.Column<int>(type: "int", nullable: false),
                    dayOfWeek = table.Column<int>(type: "int", nullable: true),
                    specificDate = table.Column<DateOnly>(type: "date", nullable: true),
                    dayOfMonth = table.Column<int>(type: "int", nullable: true),
                    timeStart = table.Column<TimeOnly>(type: "time", nullable: false),
                    timeEnd = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCHMAKING_QUEUE_SLOT", x => x.matchmakingQueueSlotId);
                    table.ForeignKey(
                        name: "FK_MATCHMAKING_QUEUE_SLOT_QUEUE",
                        column: x => x.matchmakingQueueId,
                        principalTable: "MATCHMAKING_QUEUE",
                        principalColumn: "matchmakingQueueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MATCHMAKING_QUEUE_matchId",
                table: "MATCHMAKING_QUEUE",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCHMAKING_QUEUE_playerId",
                table: "MATCHMAKING_QUEUE",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCHMAKING_QUEUE_SLOT_matchmakingQueueId",
                table: "MATCHMAKING_QUEUE_SLOT",
                column: "matchmakingQueueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MATCHMAKING_QUEUE_SLOT");

            migrationBuilder.DropTable(
                name: "MATCHMAKING_QUEUE");
        }
    }
}
