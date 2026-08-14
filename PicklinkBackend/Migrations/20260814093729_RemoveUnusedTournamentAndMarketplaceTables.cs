using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedTournamentAndMarketplaceTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MATCH_TEAM1",
                table: "MATCH");

            migrationBuilder.DropForeignKey(
                name: "FK_MATCH_TEAM2",
                table: "MATCH");

            migrationBuilder.DropForeignKey(
                name: "FK_MATCH_WINNER",
                table: "MATCH");

            migrationBuilder.DropTable(
                name: "INVENTORY_ITEM");

            migrationBuilder.DropTable(
                name: "MATCH_SLOT_VOTE");

            migrationBuilder.DropTable(
                name: "PLAYER_TEAM_ROSTER");

            migrationBuilder.DropTable(
                name: "SKILL_MATCHUP");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_MATCH");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_PAYMENT");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_TEAM");

            migrationBuilder.DropTable(
                name: "MARKETPLACE_PROVIDER");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_REGISTRATION");

            migrationBuilder.DropTable(
                name: "TEAM");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_DIVISION");

            migrationBuilder.DropTable(
                name: "TOURNAMENT");

            migrationBuilder.DropIndex(
                name: "IX_MATCH_team1Id",
                table: "MATCH");

            migrationBuilder.DropIndex(
                name: "IX_MATCH_team2Id",
                table: "MATCH");

            migrationBuilder.DropIndex(
                name: "IX_MATCH_winningTeamId",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "team1Id",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "team2Id",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "winningTeamId",
                table: "MATCH");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "team1Id",
                table: "MATCH",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "team2Id",
                table: "MATCH",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "winningTeamId",
                table: "MATCH",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MARKETPLACE_PROVIDER",
                columns: table => new
                {
                    providerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<int>(type: "int", nullable: false),
                    providerType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    specialty = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKETPLACE_PROVIDER", x => x.providerId);
                    table.ForeignKey(
                        name: "FK_MARKETPLACE_PROVIDER_USER",
                        column: x => x.userId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "MATCH_SLOT_VOTE",
                columns: table => new
                {
                    matchSlotVoteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    playerId = table.Column<int>(type: "int", nullable: false),
                    courtId = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    endTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    startTime = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MATCH_SLOT_VOTE", x => x.matchSlotVoteId);
                    table.CheckConstraint("CK_MATCH_SLOT_VOTE_time", "[endTime] > [startTime]");
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_VOTE_COURT",
                        column: x => x.courtId,
                        principalTable: "COURT",
                        principalColumn: "courtId");
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_VOTE_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MATCH_SLOT_VOTE_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SKILL_MATCHUP",
                columns: table => new
                {
                    matchupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    matchId = table.Column<int>(type: "int", nullable: false),
                    playerId = table.Column<int>(type: "int", nullable: false),
                    skillDelta = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SKILL_MATCHUP", x => x.matchupId);
                    table.ForeignKey(
                        name: "FK_SKILL_MATCHUP_MATCH",
                        column: x => x.matchId,
                        principalTable: "MATCH",
                        principalColumn: "matchId");
                    table.ForeignKey(
                        name: "FK_SKILL_MATCHUP_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "TEAM",
                columns: table => new
                {
                    teamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    captainId = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    teamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TEAM", x => x.teamId);
                    table.ForeignKey(
                        name: "FK_TEAM_CAPTAIN",
                        column: x => x.captainId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                });

            migrationBuilder.CreateTable(
                name: "TOURNAMENT",
                columns: table => new
                {
                    tournamentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    approvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    approvedByUserId = table.Column<int>(type: "int", nullable: true),
                    bracketType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    createdByUserId = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    endDate = table.Column<DateOnly>(type: "date", nullable: false),
                    entryFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    format = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    imageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    organizerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    organizerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    prizePool = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    registrationDeadline = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resultsPublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    rules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    skillLevel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    slug = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    startDate = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Draft"),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    venueName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT", x => x.tournamentId);
                });

            migrationBuilder.CreateTable(
                name: "INVENTORY_ITEM",
                columns: table => new
                {
                    itemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    providerId = table.Column<int>(type: "int", nullable: false),
                    itemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    pricePerUnit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Available")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_INVENTORY_ITEM", x => x.itemId);
                    table.ForeignKey(
                        name: "FK_INVENTORY_ITEM_PROVIDER",
                        column: x => x.providerId,
                        principalTable: "MARKETPLACE_PROVIDER",
                        principalColumn: "providerId");
                });

            migrationBuilder.CreateTable(
                name: "PLAYER_TEAM_ROSTER",
                columns: table => new
                {
                    playerId = table.Column<int>(type: "int", nullable: false),
                    teamId = table.Column<int>(type: "int", nullable: false),
                    joinedDate = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "(CONVERT([date],getdate()))")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PLAYER_TEAM_ROSTER", x => new { x.playerId, x.teamId });
                    table.ForeignKey(
                        name: "FK_PTR_PLAYER",
                        column: x => x.playerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                    table.ForeignKey(
                        name: "FK_PTR_TEAM",
                        column: x => x.teamId,
                        principalTable: "TEAM",
                        principalColumn: "teamId");
                });

            migrationBuilder.CreateTable(
                name: "TOURNAMENT_DIVISION",
                columns: table => new
                {
                    tournamentDivisionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    displayOrder = table.Column<int>(type: "int", nullable: false),
                    entryFee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    skillLevel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Open")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT_DIVISION", x => x.tournamentDivisionId);
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_DIVISION_TOURNAMENT",
                        column: x => x.tournamentId,
                        principalTable: "TOURNAMENT",
                        principalColumn: "tournamentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TOURNAMENT_TEAM",
                columns: table => new
                {
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    teamId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT_TEAM", x => new { x.tournamentId, x.teamId });
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_TEAM_TEAM",
                        column: x => x.teamId,
                        principalTable: "TEAM",
                        principalColumn: "teamId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_TEAM_TOURN",
                        column: x => x.tournamentId,
                        principalTable: "TOURNAMENT",
                        principalColumn: "tournamentId");
                });

            migrationBuilder.CreateTable(
                name: "TOURNAMENT_REGISTRATION",
                columns: table => new
                {
                    tournamentRegistrationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    captainPlayerId = table.Column<int>(type: "int", nullable: false),
                    tournamentDivisionId = table.Column<int>(type: "int", nullable: false),
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    amountDue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    checkInCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    checkedInAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    checkedInByUserId = table.Column<int>(type: "int", nullable: true),
                    partnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    paymentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Unpaid"),
                    registeredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    rejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    representativePhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    reviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    seed = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    teamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT_REGISTRATION", x => x.tournamentRegistrationId);
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_REGISTRATION_DIVISION",
                        column: x => x.tournamentDivisionId,
                        principalTable: "TOURNAMENT_DIVISION",
                        principalColumn: "tournamentDivisionId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_REGISTRATION_PLAYER",
                        column: x => x.captainPlayerId,
                        principalTable: "PLAYER",
                        principalColumn: "playerId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_REGISTRATION_TOURNAMENT",
                        column: x => x.tournamentId,
                        principalTable: "TOURNAMENT",
                        principalColumn: "tournamentId");
                });

            migrationBuilder.CreateTable(
                name: "TOURNAMENT_MATCH",
                columns: table => new
                {
                    tournamentMatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    team1RegistrationId = table.Column<int>(type: "int", nullable: true),
                    team2RegistrationId = table.Column<int>(type: "int", nullable: true),
                    tournamentDivisionId = table.Column<int>(type: "int", nullable: false),
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    winnerRegistrationId = table.Column<int>(type: "int", nullable: true),
                    courtName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    matchNumber = table.Column<int>(type: "int", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    roundName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    scheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Scheduled"),
                    team1Score = table.Column<int>(type: "int", nullable: true),
                    team2Score = table.Column<int>(type: "int", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT_MATCH", x => x.tournamentMatchId);
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_MATCH_DIVISION",
                        column: x => x.tournamentDivisionId,
                        principalTable: "TOURNAMENT_DIVISION",
                        principalColumn: "tournamentDivisionId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_MATCH_TEAM1",
                        column: x => x.team1RegistrationId,
                        principalTable: "TOURNAMENT_REGISTRATION",
                        principalColumn: "tournamentRegistrationId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_MATCH_TEAM2",
                        column: x => x.team2RegistrationId,
                        principalTable: "TOURNAMENT_REGISTRATION",
                        principalColumn: "tournamentRegistrationId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_MATCH_TOURNAMENT",
                        column: x => x.tournamentId,
                        principalTable: "TOURNAMENT",
                        principalColumn: "tournamentId");
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_MATCH_WINNER",
                        column: x => x.winnerRegistrationId,
                        principalTable: "TOURNAMENT_REGISTRATION",
                        principalColumn: "tournamentRegistrationId");
                });

            migrationBuilder.CreateTable(
                name: "TOURNAMENT_PAYMENT",
                columns: table => new
                {
                    tournamentPaymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tournamentRegistrationId = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    paymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    receiptImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    rejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    submittedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    transferContent = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    verifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    verifiedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TOURNAMENT_PAYMENT", x => x.tournamentPaymentId);
                    table.ForeignKey(
                        name: "FK_TOURNAMENT_PAYMENT_REGISTRATION",
                        column: x => x.tournamentRegistrationId,
                        principalTable: "TOURNAMENT_REGISTRATION",
                        principalColumn: "tournamentRegistrationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_team1Id",
                table: "MATCH",
                column: "team1Id");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_team2Id",
                table: "MATCH",
                column: "team2Id");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_winningTeamId",
                table: "MATCH",
                column: "winningTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_INVENTORY_ITEM_providerId",
                table: "INVENTORY_ITEM",
                column: "providerId");

            migrationBuilder.CreateIndex(
                name: "IX_MARKETPLACE_PROVIDER_userId",
                table: "MARKETPLACE_PROVIDER",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_VOTE_court_time",
                table: "MATCH_SLOT_VOTE",
                columns: new[] { "courtId", "startTime", "endTime" });

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_VOTE_matchId",
                table: "MATCH_SLOT_VOTE",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_MATCH_SLOT_VOTE_playerId",
                table: "MATCH_SLOT_VOTE",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "UQ_MATCH_SLOT_VOTE_player_slot",
                table: "MATCH_SLOT_VOTE",
                columns: new[] { "matchId", "playerId", "courtId", "startTime", "endTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PLAYER_TEAM_ROSTER_teamId",
                table: "PLAYER_TEAM_ROSTER",
                column: "teamId");

            migrationBuilder.CreateIndex(
                name: "IX_SKILL_MATCHUP_matchId",
                table: "SKILL_MATCHUP",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_SKILL_MATCHUP_playerId",
                table: "SKILL_MATCHUP",
                column: "playerId");

            migrationBuilder.CreateIndex(
                name: "IX_TEAM_captainId",
                table: "TEAM",
                column: "captainId");

            migrationBuilder.CreateIndex(
                name: "UQ_TOURNAMENT_slug",
                table: "TOURNAMENT",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TOURNAMENT_DIVISION_name",
                table: "TOURNAMENT_DIVISION",
                columns: new[] { "tournamentId", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_MATCH_team1RegistrationId",
                table: "TOURNAMENT_MATCH",
                column: "team1RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_MATCH_team2RegistrationId",
                table: "TOURNAMENT_MATCH",
                column: "team2RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_MATCH_tournamentId",
                table: "TOURNAMENT_MATCH",
                column: "tournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_MATCH_winnerRegistrationId",
                table: "TOURNAMENT_MATCH",
                column: "winnerRegistrationId");

            migrationBuilder.CreateIndex(
                name: "UQ_TOURNAMENT_MATCH_round",
                table: "TOURNAMENT_MATCH",
                columns: new[] { "tournamentDivisionId", "roundName", "matchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TOURNAMENT_PAYMENT_registration",
                table: "TOURNAMENT_PAYMENT",
                column: "tournamentRegistrationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_REGISTRATION_captainPlayerId",
                table: "TOURNAMENT_REGISTRATION",
                column: "captainPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_REGISTRATION_tournamentDivisionId",
                table: "TOURNAMENT_REGISTRATION",
                column: "tournamentDivisionId");

            migrationBuilder.CreateIndex(
                name: "UQ_TOURNAMENT_REGISTRATION_captain",
                table: "TOURNAMENT_REGISTRATION",
                columns: new[] { "tournamentId", "captainPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_TOURNAMENT_REGISTRATION_checkInCode",
                table: "TOURNAMENT_REGISTRATION",
                column: "checkInCode",
                unique: true,
                filter: "[checkInCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TOURNAMENT_TEAM_teamId",
                table: "TOURNAMENT_TEAM",
                column: "teamId");

            migrationBuilder.AddForeignKey(
                name: "FK_MATCH_TEAM1",
                table: "MATCH",
                column: "team1Id",
                principalTable: "TEAM",
                principalColumn: "teamId");

            migrationBuilder.AddForeignKey(
                name: "FK_MATCH_TEAM2",
                table: "MATCH",
                column: "team2Id",
                principalTable: "TEAM",
                principalColumn: "teamId");

            migrationBuilder.AddForeignKey(
                name: "FK_MATCH_WINNER",
                table: "MATCH",
                column: "winningTeamId",
                principalTable: "TEAM",
                principalColumn: "teamId");
        }
    }
}
