using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentSponsorshipClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allowPaymentByOthers",
                table: "PAYMENT",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "claimExpiresAt",
                table: "PAYMENT",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "claimedByPlayerId",
                table: "PAYMENT",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowPaymentByOthers",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "claimExpiresAt",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "claimedByPlayerId",
                table: "PAYMENT");
        }
    }
}
