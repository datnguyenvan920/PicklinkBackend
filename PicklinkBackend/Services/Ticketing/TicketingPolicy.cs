namespace PicklinkBackend.Services.Ticketing;

public static class TicketingPolicy
{
    public static bool OccupiesCapacity(string status, DateTime? holdExpiresAt, DateTime utcNow) =>
        status is "Paid" or "CheckedIn"
        || status == "PendingPayment" && holdExpiresAt > utcNow;

    public static string EffectiveTicketStatus(string status, DateTime? holdExpiresAt, DateTime utcNow) =>
        status == "PendingPayment" && (!holdExpiresAt.HasValue || holdExpiresAt <= utcNow)
            ? "Expired"
            : status;

    public static bool CanPlayerCancel(DateTime startTime, DateTime localNow, int deadlineHours) =>
        localNow <= startTime.AddHours(-Math.Max(0, deadlineHours));

    public static bool CanCheckIn(DateTime startTime, DateTime endTime, DateTime localNow, int openMinutes) =>
        localNow >= startTime.AddMinutes(-Math.Max(0, openMinutes)) && localNow <= endTime;
}
