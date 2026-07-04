using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705002333_AddPaymentGroupId")]
    public partial class AddPaymentGroupId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "paymentGroupId",
                table: "PAYMENT",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENT_paymentGroupId",
                table: "PAYMENT",
                column: "paymentGroupId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PAYMENT_paymentGroupId",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "paymentGroupId",
                table: "PAYMENT");
        }
    }
}
