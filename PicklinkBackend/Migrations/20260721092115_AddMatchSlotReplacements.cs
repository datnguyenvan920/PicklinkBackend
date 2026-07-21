using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchSlotReplacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MATCH_SLOT_ABSENCE",
                columns: table => new
                {
                    matchSlotAbsenceId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    bookingCheckInGroupId = table.Column<int>(type: "int", nullable: false),
                    unavailablePlayerId = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Open"),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCH_SLOT_ABSENCE", x => x.matchSlotAbsenceId);
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_ABSENCE_GROUP",
                        column: x => x.bookingCheckInGroupId,
                        principalTable: "BOOKING_CHECKIN_GROUP",
                        principalColumn: "bookingCheckInGroupId");
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_ABSENCE_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_ABSENCE_PLAYER",
                        column: x => x.unavailablePlayerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "MATCH_SLOT_REPLACEMENT_REQUEST",
                columns: table => new
                {
                    matchSlotReplacementRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchSlotAbsenceId = table.Column<int>(type: "int", nullable: false),
                    playerId = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    requestedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    respondedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCH_SLOT_REPLACEMENT_REQUEST", x => x.matchSlotReplacementRequestId);
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_REPLACEMENT_ABSENCE",
                        column: x => x.matchSlotAbsenceId,
                        principalTable: "MATCH_SLOT_ABSENCE",
                        principalColumn: "matchSlotAbsenceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_REPLACEMENT_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_ABSENCE_group",
                table: "MATCH_SLOT_ABSENCE",
                column: "bookingCheckInGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_ABSENCE_match",
                table: "MATCH_SLOT_ABSENCE",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_ABSENCE_player",
                table: "MATCH_SLOT_ABSENCE",
                column: "unavailablePlayerId");

            migrationBuilder.CreateIndex(
                name: "UQ_MATCH_SLOT_ABSENCE_group_player",
                table: "MATCH_SLOT_ABSENCE",
                columns: new[] { "bookingCheckInGroupId", "unavailablePlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_REPLACEMENT_absence",
                table: "MATCH_SLOT_REPLACEMENT_REQUEST",
                column: "matchSlotAbsenceId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_REPLACEMENT_player",
                table: "MATCH_SLOT_REPLACEMENT_REQUEST",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "UQ_MATCH_SLOT_REPLACEMENT_absence_player",
                table: "MATCH_SLOT_REPLACEMENT_REQUEST",
                columns: new[] { "matchSlotAbsenceId", "playerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MATCH_SLOT_REPLACEMENT_REQUEST");

            migrationBuilder.DropTable(
                name: "MATCH_SLOT_ABSENCE");
        }
    }
}
