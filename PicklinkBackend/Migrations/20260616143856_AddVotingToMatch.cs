using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddVotingToMatch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "votedEndTime",
                table: "MATCH_PARTICIPANT",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "votedStartTime",
                table: "MATCH_PARTICIPANT",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "votedVenueId",
                table: "MATCH_PARTICIPANT",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "matchTime",
                table: "MATCH",
                type: "datetime",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "preferredTimeEnd",
                table: "MATCH",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "preferredTimeStart",
                table: "MATCH",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sharedVenues",
                table: "MATCH",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "votedEndTime",
                table: "MATCH_PARTICIPANT");

            migrationBuilder.DropColumn(
                name: "votedStartTime",
                table: "MATCH_PARTICIPANT");

            migrationBuilder.DropColumn(
                name: "votedVenueId",
                table: "MATCH_PARTICIPANT");

            migrationBuilder.DropColumn(
                name: "preferredTimeEnd",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "preferredTimeStart",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "sharedVenues",
                table: "MATCH");

            migrationBuilder.AlterColumn<DateTime>(
                name: "matchTime",
                table: "MATCH",
                type: "datetime",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime",
                oldNullable: true);
        }
    }
}
