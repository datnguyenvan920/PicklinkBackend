using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddSePayTransactionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SEPAY_TRANSACTION",
                columns: table => new
                {
                    sePayTransactionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    externalTransactionId = table.Column<long>(type: "bigint", nullable: false),
                    paymentId = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    accountNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    referenceCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    receivedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    refundedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    refundReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SEPAY_TRANSACTION", x => x.sePayTransactionId);
                    table.CheckConstraint("CK_SEPAY_TRANSACTION_status", "[status] IN ('Applied','TicketRefundPending','AdditionalRefundPending','Refunded','ReviewRequired')");
                    table.ForeignKey(
                        name: "FK_SEPAY_TRANSACTION_PAYMENT",
                        column: x => x.paymentId,
                        principalTable: "PAYMENT",
                        principalColumn: "paymentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SEPAY_TRANSACTION_payment_status",
                table: "SEPAY_TRANSACTION",
                columns: new[] { "paymentId", "status", "receivedAt" });

            migrationBuilder.CreateIndex(
                name: "UQ_SEPAY_TRANSACTION_externalId",
                table: "SEPAY_TRANSACTION",
                column: "externalTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SEPAY_TRANSACTION");
        }
    }
}
