using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBookingVenueAndCourt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approvalStatus",
                table: "VENUE",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<bool>(
                name: "isOpen",
                table: "VENUE",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "rejectionReason",
                table: "VENUE",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "groupId",
                table: "POST",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "courtType",
                table: "COURT",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<double>(
                name: "hourlyPrice",
                table: "COURT",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "groupId",
                table: "CONVERSATION",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "matchId",
                table: "CONVERSATION",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bookingCode",
                table: "BOOKING",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "courtAmount",
                table: "BOOKING",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "createdAt",
                table: "BOOKING",
                type: "datetime",
                nullable: false,
                defaultValueSql: "(getutcdate())");

            migrationBuilder.AddColumn<DateTime>(
                name: "holdExpiresAt",
                table: "BOOKING",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "hourlyPriceSnapshot",
                table: "BOOKING",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "ownerEntryType",
                table: "BOOKING",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "BOOKING",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "totalAmount",
                table: "BOOKING",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "BOOKING_STATUS_HISTORY",
                columns: table => new
                {
                    bookingStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    bookingId = table.Column<int>(type: "int", nullable: false),
                    fromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    toStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    actorUserId = table.Column<int>(type: "int", nullable: true),
                    changedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_STATUS_HISTORY", x => x.bookingStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_BOOKING_STATUS_HISTORY_BOOKING",
                        column: x => x.bookingId,
                        principalTable: "BOOKING",
                        principalColumn: "bookingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VENUE_IMAGE",
                columns: table => new
                {
                    venueImageId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    venueId = table.Column<int>(type: "int", nullable: false),
                    imageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    caption = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    isPrimary = table.Column<bool>(type: "bit", nullable: false),
                    sortOrder = table.Column<int>(type: "int", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENUE_IMAGE", x => x.venueImageId);
                    table.ForeignKey(
                        name: "FK_VENUE_IMAGE_VENUE",
                        column: x => x.venueId,
                        principalTable: "VENUE",
                        principalColumn: "venueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_POST_groupId",
                table: "POST",
                column: "groupId",
                filter: "([groupId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_groupId",
                table: "CONVERSATION",
                column: "groupId",
                filter: "([groupId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CONVERSATION_matchId",
                table: "CONVERSATION",
                column: "matchId");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_STATUS_HISTORY_bookingId",
                table: "BOOKING_STATUS_HISTORY",
                column: "bookingId");

            migrationBuilder.CreateIndex(
                name: "IX_VENUE_IMAGE_venueId",
                table: "VENUE_IMAGE",
                column: "venueId");

            migrationBuilder.AddForeignKey(
                name: "FK_CONVERSATION_MATCH",
                table: "CONVERSATION",
                column: "matchId",
                principalTable: "MATCH",
                principalColumn: "matchId");

            migrationBuilder.AddForeignKey(
                name: "FK_CONVERSATION_SOCIAL_GROUP",
                table: "CONVERSATION",
                column: "groupId",
                principalTable: "SOCIAL_GROUP",
                principalColumn: "groupId");

            migrationBuilder.AddForeignKey(
                name: "FK_POST_SOCIAL_GROUP",
                table: "POST",
                column: "groupId",
                principalTable: "SOCIAL_GROUP",
                principalColumn: "groupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CONVERSATION_MATCH",
                table: "CONVERSATION");

            migrationBuilder.DropForeignKey(
                name: "FK_CONVERSATION_SOCIAL_GROUP",
                table: "CONVERSATION");

            migrationBuilder.DropForeignKey(
                name: "FK_POST_SOCIAL_GROUP",
                table: "POST");

            migrationBuilder.DropTable(
                name: "BOOKING_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "VENUE_IMAGE");

            migrationBuilder.DropIndex(
                name: "IX_POST_groupId",
                table: "POST");

            migrationBuilder.DropIndex(
                name: "IX_CONVERSATION_groupId",
                table: "CONVERSATION");

            migrationBuilder.DropIndex(
                name: "IX_CONVERSATION_matchId",
                table: "CONVERSATION");

            migrationBuilder.DropColumn(
                name: "approvalStatus",
                table: "VENUE");

            migrationBuilder.DropColumn(
                name: "isOpen",
                table: "VENUE");

            migrationBuilder.DropColumn(
                name: "rejectionReason",
                table: "VENUE");

            migrationBuilder.DropColumn(
                name: "groupId",
                table: "POST");

            migrationBuilder.DropColumn(
                name: "courtType",
                table: "COURT");

            migrationBuilder.DropColumn(
                name: "hourlyPrice",
                table: "COURT");

            migrationBuilder.DropColumn(
                name: "groupId",
                table: "CONVERSATION");

            migrationBuilder.DropColumn(
                name: "matchId",
                table: "CONVERSATION");

            migrationBuilder.DropColumn(
                name: "bookingCode",
                table: "BOOKING");

            migrationBuilder.DropColumn(
                name: "courtAmount",
                table: "BOOKING");

            migrationBuilder.DropColumn(
                name: "createdAt",
                table: "BOOKING");

            migrationBuilder.DropColumn(
                name: "holdExpiresAt",
                table: "BOOKING");

            migrationBuilder.DropColumn(
                name: "hourlyPriceSnapshot",
                table: "BOOKING");

            migrationBuilder.DropColumn(
                name: "ownerEntryType",
                table: "BOOKING");

            migrationBuilder.DropColumn(
                name: "title",
                table: "BOOKING");

            migrationBuilder.DropColumn(
                name: "totalAmount",
                table: "BOOKING");
        }
    }
}
