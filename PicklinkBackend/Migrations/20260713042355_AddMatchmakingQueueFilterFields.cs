using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchmakingQueueFilterFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "isPublic",
                table: "MATCHMAKING_QUEUE",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "province",
                table: "MATCHMAKING_QUEUE",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sharedVenues",
                table: "MATCHMAKING_QUEUE",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updatedAt",
                table: "MATCHMAKING_QUEUE",
                type: "datetime",
                nullable: false,
                defaultValueSql: "(getutcdate())");

            migrationBuilder.AddColumn<string>(
                name: "ward",
                table: "MATCHMAKING_QUEUE",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "isPublic",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "province",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "sharedVenues",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "updatedAt",
                table: "MATCHMAKING_QUEUE");

            migrationBuilder.DropColumn(
                name: "ward",
                table: "MATCHMAKING_QUEUE");
        }
    }
}
