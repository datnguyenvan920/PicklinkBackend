using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class Phase10TournamentStep1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "TOURNAMENT",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Draft",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Upcoming");

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "TOURNAMENT",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "approvedAt",
                table: "TOURNAMENT",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "approvedByUserId",
                table: "TOURNAMENT",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bracketType",
                table: "TOURNAMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "capacity",
                table: "TOURNAMENT",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "TOURNAMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "createdAt",
                table: "TOURNAMENT",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "(getutcdate())");

            migrationBuilder.AddColumn<int>(
                name: "createdByUserId",
                table: "TOURNAMENT",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "TOURNAMENT",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "entryFee",
                table: "TOURNAMENT",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "format",
                table: "TOURNAMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "imageUrl",
                table: "TOURNAMENT",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "organizerName",
                table: "TOURNAMENT",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "organizerPhone",
                table: "TOURNAMENT",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "prizePool",
                table: "TOURNAMENT",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "registrationDeadline",
                table: "TOURNAMENT",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "resultsPublishedAt",
                table: "TOURNAMENT",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rules",
                table: "TOURNAMENT",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "skillLevel",
                table: "TOURNAMENT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "TOURNAMENT",
                type: "nvarchar(220)",
                maxLength: 220,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "updatedAt",
                table: "TOURNAMENT",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "(getutcdate())");

            migrationBuilder.AddColumn<string>(
                name: "venueName",
                table: "TOURNAMENT",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TOURNAMENT_DIVISION",
                columns: table => new
                {
                    tournamentDivisionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    skillLevel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    entryFee = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Open"),
                    displayOrder = table.Column<int>(type: "int", nullable: false)
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
                name: "TOURNAMENT_REGISTRATION",
                columns: table => new
                {
                    tournamentRegistrationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    tournamentDivisionId = table.Column<int>(type: "int", nullable: false),
                    captainPlayerId = table.Column<int>(type: "int", nullable: false),
                    teamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    partnerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    representativePhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    paymentStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Unpaid"),
                    amountDue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    registeredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    reviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    reviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    rejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    checkInCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    checkedInAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    checkedInByUserId = table.Column<int>(type: "int", nullable: true),
                    seed = table.Column<int>(type: "int", nullable: true)
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
                    tournamentId = table.Column<int>(type: "int", nullable: false),
                    tournamentDivisionId = table.Column<int>(type: "int", nullable: false),
                    roundName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    matchNumber = table.Column<int>(type: "int", nullable: false),
                    team1RegistrationId = table.Column<int>(type: "int", nullable: true),
                    team2RegistrationId = table.Column<int>(type: "int", nullable: true),
                    scheduledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    courtName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    team1Score = table.Column<int>(type: "int", nullable: true),
                    team2Score = table.Column<int>(type: "int", nullable: true),
                    winnerRegistrationId = table.Column<int>(type: "int", nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Scheduled"),
                    notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
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
                    transferContent = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    receiptImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    submittedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "(getutcdate())"),
                    verifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    verifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    rejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
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

            migrationBuilder.Sql("""
                UPDATE [TOURNAMENT]
                SET
                    [slug] = CONCAT(N'tournament-', [tournamentId]),
                    [venueName] = CASE WHEN [venueName] = N'' THEN [name] ELSE [venueName] END,
                    [address] = CASE WHEN [address] = N'' THEN N'Chưa cập nhật' ELSE [address] END,
                    [city] = CASE WHEN [city] = N'' THEN N'Chưa cập nhật' ELSE [city] END,
                    [organizerName] = CASE WHEN [organizerName] = N'' THEN N'Picklink' ELSE [organizerName] END,
                    [format] = CASE WHEN [format] = N'' THEN N'Chưa cấu hình' ELSE [format] END,
                    [bracketType] = CASE WHEN [bracketType] = N'' THEN N'Nhập kết quả thủ công' ELSE [bracketType] END,
                    [capacity] = CASE WHEN [capacity] = 0 THEN 32 ELSE [capacity] END,
                    [registrationDeadline] = DATEADD(day, -1, CAST([startDate] AS datetime2)),
                    [status] = CASE
                        WHEN [status] = N'Upcoming' AND [startDate] >= CAST(GETUTCDATE() AS date) THEN N'Open'
                        WHEN [status] = N'Upcoming' THEN N'Completed'
                        ELSE [status]
                    END
                """);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TOURNAMENT_MATCH");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_PAYMENT");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_REGISTRATION");

            migrationBuilder.DropTable(
                name: "TOURNAMENT_DIVISION");

            migrationBuilder.DropIndex(
                name: "UQ_TOURNAMENT_slug",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "address",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "approvedAt",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "approvedByUserId",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "bracketType",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "capacity",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "city",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "createdAt",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "createdByUserId",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "description",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "entryFee",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "format",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "imageUrl",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "organizerName",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "organizerPhone",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "prizePool",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "registrationDeadline",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "resultsPublishedAt",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "rules",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "skillLevel",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "slug",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "updatedAt",
                table: "TOURNAMENT");

            migrationBuilder.DropColumn(
                name: "venueName",
                table: "TOURNAMENT");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "TOURNAMENT",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Upcoming",
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldDefaultValue: "Draft");
        }
    }
}
