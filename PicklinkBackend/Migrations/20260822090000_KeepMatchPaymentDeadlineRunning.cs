using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260822090000_KeepMatchPaymentDeadlineRunning")]
public partial class KeepMatchPaymentDeadlineRunning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE [BOOKING]
            SET [holdExpiresAt] = DATEADD(MINUTE, 20, [createdAt]),
                [holdRemainingSeconds] = NULL
            WHERE [matchId] IS NOT NULL
                AND [status] = N'Holding'
                AND [holdRemainingSeconds] IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The original deadline cannot be converted back into a safely paused duration.
    }
}
