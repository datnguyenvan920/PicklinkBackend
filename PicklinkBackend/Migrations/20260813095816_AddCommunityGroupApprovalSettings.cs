using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityGroupApprovalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requireMemberApproval",
                table: "SOCIAL_GROUP",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "requirePostApproval",
                table: "SOCIAL_GROUP",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requireMemberApproval",
                table: "SOCIAL_GROUP");

            migrationBuilder.DropColumn(
                name: "requirePostApproval",
                table: "SOCIAL_GROUP");
        }
    }
}
