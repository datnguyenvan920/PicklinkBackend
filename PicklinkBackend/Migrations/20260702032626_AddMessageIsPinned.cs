using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PicklinkBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageIsPinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "isPinned",
                table: "MESSAGE",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "availableDateFrom",
                table: "MATCH",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "availableDateTo",
                table: "MATCH",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maxSkillLevel",
                table: "MATCH",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "minSkillLevel",
                table: "MATCH",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "province",
                table: "MATCH",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "searchLatitude",
                table: "MATCH",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "searchLongitude",
                table: "MATCH",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "searchRadiusKm",
                table: "MATCH",
                type: "float",
                nullable: false,
                defaultValue: 5.0);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "MATCH",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ward",
                table: "MATCH",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "isPinned",
                table: "MESSAGE");

            migrationBuilder.DropColumn(
                name: "availableDateFrom",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "availableDateTo",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "maxSkillLevel",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "minSkillLevel",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "province",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "searchLatitude",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "searchLongitude",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "searchRadiusKm",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "title",
                table: "MATCH");

            migrationBuilder.DropColumn(
                name: "ward",
                table: "MATCH");
        }
    }
}
