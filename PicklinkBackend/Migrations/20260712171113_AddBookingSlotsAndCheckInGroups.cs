using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingSlotsAndCheckInGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "isLocked",
                table: "USER",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "isHidden",
                table: "RATING_HISTORY",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "moderatedAt",
                table: "RATING_HISTORY",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "moderatedByUserId",
                table: "RATING_HISTORY",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "moderationNote",
                table: "RATING_HISTORY",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "moderationStatus",
                table: "RATING_HISTORY",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Visible");

            migrationBuilder.AddColumn<DateTime>(
                name: "createdAt",
                table: "NOTIFICATION_LOG",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "(getutcdate())");

            migrationBuilder.AddColumn<string>(
                name: "linkLabel",
                table: "NOTIFICATION_LOG",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "linkTo",
                table: "NOTIFICATION_LOG",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notificationType",
                table: "NOTIFICATION_LOG",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "NOTIFICATION_LOG",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "Thông báo");

            migrationBuilder.AddColumn<string>(
                name: "tone",
                table: "NOTIFICATION_LOG",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.CreateTable(
                name: "BOOKING_CHECKIN_GROUP",
                columns: table => new
                {
                    bookingCheckInGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    bookingId = table.Column<int>(type: "int", nullable: false),
                    courtId = table.Column<int>(type: "int", nullable: false),
                    startTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    endTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    checkInCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    checkInStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Ready"),
                    codeVerifiedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    codeVerifiedByUserId = table.Column<int>(type: "int", nullable: true),
                    checkedInAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    checkedInByUserId = table.Column<int>(type: "int", nullable: true),
                    noShowAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    noShowByUserId = table.Column<int>(type: "int", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_CHECKIN_GROUP", x => x.bookingCheckInGroupId);
                    table.ForeignKey(
                        name: "FK_BOOKING_CHECKIN_GROUP_BOOKING_bookingId",
                        column: x => x.bookingId,
                        principalTable: "BOOKING",
                        principalColumn: "bookingId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BOOKING_CHECKIN_GROUP_COURT_courtId",
                        column: x => x.courtId,
                        principalTable: "COURT",
                        principalColumn: "courtId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "COMMUNITY_REPORT",
                columns: table => new
                {
                    communityReportId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    reporterUserId = table.Column<int>(type: "int", nullable: false),
                    targetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    targetId = table.Column<int>(type: "int", nullable: true),
                    targetLabel = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Open"),
                    priority = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Normal"),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    reviewedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    reviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    resolutionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_COMMUNITY_REPORT", x => x.communityReportId);
                    table.ForeignKey(
                        name: "FK_COMMUNITY_REPORT_REPORTER",
                        column: x => x.reporterUserId,
                        principalTable: "USER",
                        principalColumn: "userId");
                    table.ForeignKey(
                        name: "FK_COMMUNITY_REPORT_REVIEWER",
                        column: x => x.reviewedByUserId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "LISTING_FEE_SETTING",
                columns: table => new
                {
                    listingFeeSettingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    pricePerCourtPerMonth = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    updatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LISTING_FEE_SETTING", x => x.listingFeeSettingId);
                    table.ForeignKey(
                        name: "FK_LISTING_FEE_SETTING_USER",
                        column: x => x.updatedByUserId,
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
                    startTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    endTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())")
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
                name: "PLATFORM_SETTING",
                columns: table => new
                {
                    platformSettingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    settingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    settingValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    settingGroup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "General"),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: ""),
                    updatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    updatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PLATFORM_SETTING", x => x.platformSettingId);
                    table.ForeignKey(
                        name: "FK_PLATFORM_SETTING_UPDATED_BY",
                        column: x => x.updatedByUserId,
                        principalTable: "USER",
                        principalColumn: "userId");
                });

            migrationBuilder.CreateTable(
                name: "VENUE_LISTING_PAYMENT",
                columns: table => new
                {
                    venueListingPaymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    months = table.Column<int>(type: "int", nullable: false),
                    activeCourtCount = table.Column<int>(type: "int", nullable: false),
                    pricePerCourtPerMonth = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    receiptImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    rejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    submittedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                    reviewedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    reviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    paidFrom = table.Column<DateTime>(type: "datetime", nullable: true),
                    paidUntil = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENUE_LISTING_PAYMENT", x => x.venueListingPaymentId);
                    table.ForeignKey(
                        name: "FK_VENUE_LISTING_PAYMENT_REVIEWER",
                        column: x => x.reviewedByUserId,
                        principalTable: "USER",
                        principalColumn: "userId");
                    table.ForeignKey(
                        name: "FK_VENUE_LISTING_PAYMENT_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BOOKING_SLOT",
                columns: table => new
                {
                    bookingSlotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    bookingId = table.Column<int>(type: "int", nullable: false),
                    courtId = table.Column<int>(type: "int", nullable: false),
                    checkInGroupId = table.Column<int>(type: "int", nullable: true),
                    startTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    endTime = table.Column<DateTime>(type: "datetime", nullable: false),
                    hourlyPriceSnapshot = table.Column<double>(type: "float", nullable: false),
                    courtAmount = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_SLOT", x => x.bookingSlotId);
                    table.ForeignKey(
                        name: "FK_BOOKING_SLOT_BOOKING_CHECKIN_GROUP_checkInGroupId",
                        column: x => x.checkInGroupId,
                        principalTable: "BOOKING_CHECKIN_GROUP",
                        principalColumn: "bookingCheckInGroupId",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_BOOKING_SLOT_BOOKING_bookingId",
                        column: x => x.bookingId,
                        principalTable: "BOOKING",
                        principalColumn: "bookingId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BOOKING_SLOT_COURT_courtId",
                        column: x => x.courtId,
                        principalTable: "COURT",
                        principalColumn: "courtId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RATING_HISTORY_moderatedByUserId",
                table: "RATING_HISTORY",
                column: "moderatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_CHECKIN_GROUP_booking_time",
                table: "BOOKING_CHECKIN_GROUP",
                columns: new[] { "bookingId", "startTime" });

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_CHECKIN_GROUP_courtId",
                table: "BOOKING_CHECKIN_GROUP",
                column: "courtId");

            migrationBuilder.CreateIndex(
                name: "UQ_BOOKING_CHECKIN_GROUP_code",
                table: "BOOKING_CHECKIN_GROUP",
                column: "checkInCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_SLOT_booking_time",
                table: "BOOKING_SLOT",
                columns: new[] { "bookingId", "startTime" });

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_SLOT_checkInGroupId",
                table: "BOOKING_SLOT",
                column: "checkInGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_SLOT_court_time",
                table: "BOOKING_SLOT",
                columns: new[] { "courtId", "startTime", "endTime" });

            migrationBuilder.CreateIndex(
                name: "IX_COMMUNITY_REPORT_reporterUserId",
                table: "COMMUNITY_REPORT",
                column: "reporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_COMMUNITY_REPORT_reviewedByUserId",
                table: "COMMUNITY_REPORT",
                column: "reviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_COMMUNITY_REPORT_status",
                table: "COMMUNITY_REPORT",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_COMMUNITY_REPORT_targetType",
                table: "COMMUNITY_REPORT",
                column: "targetType");

            migrationBuilder.CreateIndex(
                name: "IX_LISTING_FEE_SETTING_updatedByUserId",
                table: "LISTING_FEE_SETTING",
                column: "updatedByUserId");

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
                name: "IX_PLATFORM_SETTING_updatedByUserId",
                table: "PLATFORM_SETTING",
                column: "updatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_PLATFORM_SETTING_settingKey",
                table: "PLATFORM_SETTING",
                column: "settingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_LISTING_PAYMENT_reviewedByUserId",
                table: "VENUE_LISTING_PAYMENT",
                column: "reviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_LISTING_PAYMENT_status",
                table: "VENUE_LISTING_PAYMENT",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_LISTING_PAYMENT_venueId",
                table: "VENUE_LISTING_PAYMENT",
                column: "venueId");

            migrationBuilder.AddForeignKey(
                name: "FK_RATING_HISTORY_MODERATOR",
                table: "RATING_HISTORY",
                column: "moderatedByUserId",
                principalTable: "USER",
                principalColumn: "userId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RATING_HISTORY_MODERATOR",
                table: "RATING_HISTORY");

            migrationBuilder.DropTable(
                name: "BOOKING_SLOT");

            migrationBuilder.DropTable(
                name: "COMMUNITY_REPORT");

            migrationBuilder.DropTable(
                name: "LISTING_FEE_SETTING");

            migrationBuilder.DropTable(
                name: "MATCH_SLOT_VOTE");

            migrationBuilder.DropTable(
                name: "PLATFORM_SETTING");

            migrationBuilder.DropTable(
                name: "VENUE_LISTING_PAYMENT");

            migrationBuilder.DropTable(
                name: "BOOKING_CHECKIN_GROUP");

            migrationBuilder.DropIndex(
                name: "IX_RATING_HISTORY_moderatedByUserId",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "isLocked",
                table: "USER");

            migrationBuilder.DropColumn(
                name: "isHidden",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderatedAt",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderatedByUserId",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderationNote",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "moderationStatus",
                table: "RATING_HISTORY");

            migrationBuilder.DropColumn(
                name: "createdAt",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "linkLabel",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "linkTo",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "notificationType",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "title",
                table: "NOTIFICATION_LOG");

            migrationBuilder.DropColumn(
                name: "tone",
                table: "NOTIFICATION_LOG");
        }
    }
}
