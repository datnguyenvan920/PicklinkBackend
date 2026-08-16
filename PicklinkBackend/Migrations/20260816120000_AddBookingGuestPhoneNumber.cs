using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Picklink_API.Migrations
{
    /// <summary>
    /// A walk-in booked at the counter may have no player profile, so there is nowhere to read a
    /// contact number from. This keeps one on the booking itself.
    /// </summary>
    public partial class AddBookingGuestPhoneNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'BOOKING', N'guestPhoneNumber') IS NULL
                    ALTER TABLE [BOOKING] ADD [guestPhoneNumber] nvarchar(30) NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'BOOKING', N'guestPhoneNumber') IS NOT NULL
                    ALTER TABLE [BOOKING] DROP COLUMN [guestPhoneNumber];
                """);
        }
    }
}
