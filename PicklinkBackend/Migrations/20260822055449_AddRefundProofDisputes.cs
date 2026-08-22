using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundProofDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "refundDisputeReason",
                table: "PAYMENT",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refundDisputeResolution",
                table: "PAYMENT",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refundDisputeResolvedAt",
                table: "PAYMENT",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "refundDisputeResolvedByUserId",
                table: "PAYMENT",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refundDisputeStatus",
                table: "PAYMENT",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refundDisputedAt",
                table: "PAYMENT",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refundProofImageUrl",
                table: "PAYMENT",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refundProofSubmittedAt",
                table: "PAYMENT",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refundReference",
                table: "PAYMENT",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refundDisputeReason",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundDisputeResolution",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundDisputeResolvedAt",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundDisputeResolvedByUserId",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundDisputeStatus",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundDisputedAt",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundProofImageUrl",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundProofSubmittedAt",
                table: "PAYMENT");

            migrationBuilder.DropColumn(
                name: "refundReference",
                table: "PAYMENT");
        }
    }
}
