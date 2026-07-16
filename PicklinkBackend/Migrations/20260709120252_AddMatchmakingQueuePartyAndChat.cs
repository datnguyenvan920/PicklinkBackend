using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchmakingQueuePartyAndChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MATCHMAKING_QUEUE_MATCH",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropForeignKey(
                name: "FK_MATCHMAKING_QUEUE_PLAYER",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropIndex(
                name: "IX_MATCHMAKING_QUEUE_matchId",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropIndex(
                name: "IX_MATCHMAKING_QUEUE_playerId",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "matchId",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "playerId",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.AddColumn<bool>(
                name: "isActive",
                table: "MATCHMAKING_QUEUE",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "replayType",
                table: "MATCHMAKING_QUEUE",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "replayWeekdays",
                table: "MATCHMAKING_QUEUE",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "matchmakingQueueId",
                table: "CONVERSATION",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MATCHMAKING_QUEUE_PLAYER",
                columns: table => new
                {
                    matchmakingQueuePlayerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchmakingQueueId = table.Column<int>(type: "int", nullable: false),
                    playerId = table.Column<int>(type: "int", nullable: false),
                    isHost = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCHMAKING_QUEUE_PLAYER", x => x.matchmakingQueuePlayerId);
                    table.ForeignKey(
                        name: "FK_MATCHMAKING_QUEUE_PLAYER_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MATCHMAKING_QUEUE_PLAYER_QUEUE",
                        column: x => x.matchmakingQueueId,
                        principalTable: "MATCHMAKING_QUEUE",
                        principalColumn: "matchmakingQueueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_matchmakingQueueId",
                table: "CONVERSATION",
                column: "matchmakingQueueId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCHMAKING_QUEUE_PLAYER_matchmakingQueueId_playerId",
                table: "MATCHMAKING_QUEUE_PLAYER",
                columns: new[] { "matchmakingQueueId", "playerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MATCHMAKING_QUEUE_PLAYER_playerId",
                table: "MATCHMAKING_QUEUE_PLAYER",
                column: "playerId");

            migrationBuilder.AddForeignKey(
                name: "FK_CONVERSATION_MATCHMAKING_QUEUE",
                table: "CONVERSATION",
                column: "matchmakingQueueId",
                principalTable: "MATCHMAKING_QUEUE",
                principalColumn: "matchmakingQueueId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CONVERSATION_MATCHMAKING_QUEUE",
                table: "CONVERSATION");

            migrationBuilder.DropTable(
                name: "MATCHMAKING_QUEUE_PLAYER");

            migrationBuilder.DropIndex(
                name: "IX_CONVERSATION_matchmakingQueueId",
                table: "CONVERSATION");

            migrationBuilder.DropColumn(
                name: "isActive",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "replayType",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "replayWeekdays",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "matchmakingQueueId",
                table: "CONVERSATION");

            migrationBuilder.AddColumn<int>(
                name: "matchId",
                table: "MATCHMAKING_QUEUE",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "playerId",
                table: "MATCHMAKING_QUEUE",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MATCHMAKING_QUEUE_matchId",
                table: "MATCHMAKING_QUEUE",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCHMAKING_QUEUE_playerId",
                table: "MATCHMAKING_QUEUE",
                column: "playerId");

            migrationBuilder.AddForeignKey(
                name: "FK_MATCHMAKING_QUEUE_MATCH",
                table: "MATCHMAKING_QUEUE",
                column: "matchId",
                principalTable: "MATCH",
                principalColumn: "matchId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MATCHMAKING_QUEUE_PLAYER",
                table: "MATCHMAKING_QUEUE",
                column: "playerId",
                principalTable: "PLAYER",
                principalColumn: "playerId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
