using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PicklinkBackend.Data;

#nullable disable

namespace PicklinkBackend.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260720030000_BackfillConfirmedBookingStatusHistory")]
public partial class BackfillConfirmedBookingStatusHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO [BOOKING_STATUS_HISTORY]
                ([bookingId], [fromStatus], [toStatus], [reason], [actorUserId], [changedAt])
            SELECT
                booking.[bookingId],
                N'Holding',
                N'Confirmed',
                N'Thanh toán chuyển khoản đã được xác nhận',
                paid.[verifiedByUserId],
                COALESCE(paid.[verifiedAt], paid.[paidAt], booking.[createdAt])
            FROM [BOOKING] AS booking
            CROSS APPLY (
                SELECT TOP (1)
                    payment.[verifiedByUserId],
                    payment.[verifiedAt],
                    payment.[paidAt]
                FROM [PAYMENT] AS payment
                WHERE payment.[bookingId] = booking.[bookingId]
                    AND payment.[status] = N'Paid'
                ORDER BY COALESCE(payment.[verifiedAt], payment.[paidAt]) DESC, payment.[paymentId] DESC
            ) AS paid
            WHERE booking.[playerId] IS NOT NULL
                AND booking.[matchId] IS NULL
                AND NOT EXISTS (
                    SELECT 1
                    FROM [BOOKING_STATUS_HISTORY] AS history
                    WHERE history.[bookingId] = booking.[bookingId]
                        AND history.[toStatus] = N'Confirmed'
                );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data repair is intentionally irreversible.
    }
}
