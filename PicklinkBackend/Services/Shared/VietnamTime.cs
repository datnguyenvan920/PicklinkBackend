namespace PicklinkBackend.Services.Shared;

public static class VietnamTime
{
    private static readonly TimeSpan UtcOffset = TimeSpan.FromHours(7);

    // ponytail: Schedules are Vietnam wall-clock values; add venue time zones only when the product leaves Vietnam.
    public static DateTime Now => FromUtc(DateTime.UtcNow);

    public static DateTime FromUtc(DateTime value) => DateTime.SpecifyKind(
        DateTime.SpecifyKind(value, DateTimeKind.Utc).Add(UtcOffset),
        DateTimeKind.Unspecified);

    public static DateTime ToUtc(DateTime value) => DateTime.SpecifyKind(
        value.Subtract(UtcOffset),
        DateTimeKind.Utc);
}
