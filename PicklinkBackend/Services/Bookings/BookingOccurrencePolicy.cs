namespace PicklinkBackend.Services.Bookings;

public readonly record struct BookingOccurrence(
    DateTime StartTime,
    DateTime EndTime,
    string CheckInStatus);

public static class BookingOccurrencePolicy
// ponytail: A booking stays one payment unit; only its real occurrences decide daily state.
{
    private const int CheckInLeadMinutes = 30;

    public static string GetCheckInStatus(
        string bookingStatus,
        string? storedStatus,
        IEnumerable<BookingOccurrence> occurrences,
        DateTime localNow,
        DateTime fallbackStartTime,
        DateTime fallbackEndTime,
        string inactiveStatus = "Cancelled",
        string overdueStatus = "Ready")
    {
        if (bookingStatus is "Cancelled" or "Expired") return inactiveStatus;

        var occurrenceList = occurrences.OrderBy(item => item.StartTime).ToArray();
        if (occurrenceList.Length == 0)
        {
            if (storedStatus is "CheckedIn" or "NoShow") return storedStatus;
            if (bookingStatus is not ("Confirmed" or "Completed")) return "NotOpen";
            if (localNow < fallbackStartTime.AddMinutes(-CheckInLeadMinutes)) return "NotOpen";
            return localNow <= fallbackEndTime ? "Ready" : overdueStatus;
        }

        if (bookingStatus is not ("Confirmed" or "Completed")) return "NotOpen";

        var pending = occurrenceList
            .Where(item => item.CheckInStatus is not ("CheckedIn" or "NoShow"))
            .ToArray();
        if (pending.Length == 0)
            return occurrenceList.Any(item => item.CheckInStatus == "CheckedIn") ? "CheckedIn" : "NoShow";

        if (pending.Any(item => localNow >= item.StartTime.AddMinutes(-CheckInLeadMinutes) && localNow <= item.EndTime))
            return "Ready";
        if (pending.Any(item => localNow < item.StartTime.AddMinutes(-CheckInLeadMinutes)))
            return "NotOpen";

        return overdueStatus;
    }
}
