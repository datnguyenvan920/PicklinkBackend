namespace PicklinkBackend.Services.Ticketing;

public static class TicketingPolicy
{
    // ponytail: Reuse the existing skillLevel column as a normalized range; split it only if DB-side range queries become necessary.
    public static string FormatSkillRange(int minSkillLevel, int maxSkillLevel) =>
        minSkillLevel == maxSkillLevel ? minSkillLevel.ToString() : $"{minSkillLevel}-{maxSkillLevel}";

    public static (int Min, int Max) ParseSkillRange(string value)
    {
        var parts = value.Split('-', 2);
        return int.TryParse(parts[0], out var min)
            && int.TryParse(parts[^1], out var max)
            && min is >= 1 and <= 5
            && max is >= 1 and <= 5
            && min <= max
                ? (min, max)
                : (1, 5);
    }

    public static bool AllowsSkillLevel(string range, double skillLevel)
    {
        var (min, max) = ParseSkillRange(range);
        return skillLevel >= min && skillLevel <= max;
    }

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
