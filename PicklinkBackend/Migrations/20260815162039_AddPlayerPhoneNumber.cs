using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerPhoneNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'PLAYER', N'phoneNumber') IS NULL
                    ALTER TABLE [PLAYER] ADD [phoneNumber] nvarchar(30) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "phoneNumber",
                table: "PLAYER");
        }
    }
}
