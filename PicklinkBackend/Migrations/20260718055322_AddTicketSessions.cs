using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations;

public partial class AddTicketSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TICKET_SESSION",
            columns: table => new
            {
                ticketSessionId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                bookingId = table.Column<int>(type: "int", nullable: false),
                title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                skillLevel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                playFormat = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                maxPlayers = table.Column<int>(type: "int", nullable: false),
                ticketPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                cancellationDeadlineHours = table.Column<int>(type: "int", nullable: false),
                status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                updatedAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                publishedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                cancelledAt = table.Column<DateTime>(type: "datetime", nullable: true),
                cancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TICKET_SESSION", x => x.ticketSessionId);
                table.CheckConstraint("CK_TICKET_SESSION_cancel_hours", "[cancellationDeadlineHours] >= 0");
                table.CheckConstraint("CK_TICKET_SESSION_capacity", "[maxPlayers] > 0");
                table.CheckConstraint("CK_TICKET_SESSION_price", "[ticketPrice] >= 0");
                table.CheckConstraint("CK_TICKET_SESSION_status", "[status] IN ('Draft','Published','Cancelled')");
                table.ForeignKey(
                    name: "FK_TICKET_SESSION_BOOKING",
                    column: x => x.bookingId,
                    principalTable: "BOOKING",
                    principalColumn: "bookingId",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SESSION_TICKET",
            columns: table => new
            {
                sessionTicketId = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ticketSessionId = table.Column<int>(type: "int", nullable: false),
                playerId = table.Column<int>(type: "int", nullable: false),
                paymentId = table.Column<int>(type: "int", nullable: false),
                ticketCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "PendingPayment"),
                holdExpiresAt = table.Column<DateTime>(type: "datetime", nullable: true),
                createdAt = table.Column<DateTime>(type: "datetime", nullable: false, defaultValueSql: "(getutcdate())"),
                cancelledAt = table.Column<DateTime>(type: "datetime", nullable: true),
                cancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                checkedInAt = table.Column<DateTime>(type: "datetime", nullable: true),
                checkedInByStaffId = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SESSION_TICKET", x => x.sessionTicketId);
                table.CheckConstraint(
                    "CK_SESSION_TICKET_status",
                    "[status] IN ('PendingPayment','Paid','CheckedIn','Cancelled','Expired','RefundPending','Refunded')");
                table.ForeignKey(
                    name: "FK_SESSION_TICKET_PAYMENT",
                    column: x => x.paymentId,
                    principalTable: "PAYMENT",
                    principalColumn: "paymentId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SESSION_TICKET_PLAYER",
                    column: x => x.playerId,
                    principalTable: "PLAYER",
                    principalColumn: "playerId",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SESSION_TICKET_SESSION",
                    column: x => x.ticketSessionId,
                    principalTable: "TICKET_SESSION",
                    principalColumn: "ticketSessionId",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SESSION_TICKET_STAFF",
                    column: x => x.checkedInByStaffId,
                    principalTable: "STAFF",
                    principalColumn: "staffId",
                    onDelete: ReferentialAction.NoAction);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SESSION_TICKET_checkedInByStaffId",
            table: "SESSION_TICKET",
            column: "checkedInByStaffId");
        migrationBuilder.CreateIndex(
            name: "IX_SESSION_TICKET_player_createdAt",
            table: "SESSION_TICKET",
            columns: new[] { "playerId", "createdAt" });
        migrationBuilder.CreateIndex(
            name: "IX_SESSION_TICKET_session_status_hold",
            table: "SESSION_TICKET",
            columns: new[] { "ticketSessionId", "status", "holdExpiresAt" });
        migrationBuilder.CreateIndex(
            name: "UQ_SESSION_TICKET_code",
            table: "SESSION_TICKET",
            column: "ticketCode",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UQ_SESSION_TICKET_paymentId",
            table: "SESSION_TICKET",
            column: "paymentId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UQ_SESSION_TICKET_session_player",
            table: "SESSION_TICKET",
            columns: new[] { "ticketSessionId", "playerId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_TICKET_SESSION_status_createdAt",
            table: "TICKET_SESSION",
            columns: new[] { "status", "createdAt" });
        migrationBuilder.CreateIndex(
            name: "UQ_TICKET_SESSION_bookingId",
            table: "TICKET_SESSION",
            column: "bookingId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SESSION_TICKET");
        migrationBuilder.DropTable(name: "TICKET_SESSION");
    }
}
