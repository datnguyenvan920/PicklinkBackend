namespace Picklink_API.Migrations;

/// <summary>
/// Shared by the migration and by the startup schema check so both patch legacy attendance the
/// same way. Rows written before attendance was split per check-in code carry no code at all;
/// each is attached to the code whose window covers the scan, falling back to the match's first.
/// </summary>
internal static class MatchCheckInGroupSql
{
    /// <remarks>
    /// MATCH_CHECKIN.checkedInAt is UTC while a check-in group's window is Vietnam wall clock,
    /// hence the seven hour shift before the two are compared.
    /// </remarks>
    internal const string Backfill = """
        UPDATE mc
        SET mc.[bookingCheckInGroupId] = g.[bookingCheckInGroupId]
        FROM [MATCH_CHECKIN] mc
        CROSS APPLY (
            SELECT TOP 1 cg.[bookingCheckInGroupId]
            FROM [BOOKING_CHECKIN_GROUP] cg
            INNER JOIN [BOOKING] b ON b.[bookingId] = cg.[bookingId]
            WHERE b.[matchId] = mc.[matchId]
            ORDER BY
                CASE WHEN DATEADD(hour, 7, mc.[checkedInAt])
                        BETWEEN DATEADD(minute, -30, cg.[startTime]) AND cg.[endTime]
                    THEN 0 ELSE 1 END,
                cg.[startTime],
                cg.[bookingCheckInGroupId]
        ) g
        WHERE mc.[bookingCheckInGroupId] IS NULL
            AND NOT EXISTS (
                SELECT 1 FROM [MATCH_CHECKIN] other
                WHERE other.[matchId] = mc.[matchId]
                    AND other.[playerId] = mc.[playerId]
                    AND other.[bookingCheckInGroupId] = g.[bookingCheckInGroupId]);
        """;
}
