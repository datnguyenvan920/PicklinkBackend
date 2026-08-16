namespace PicklinkBackend.Services.Owner;

/// <summary>
/// The owner entry types stored on BOOKING.ownerEntryType.
/// </summary>
/// <remarks>
/// A walk-in taken at the counter may belong to someone with no account, and PAYMENT.payerId is a
/// required foreign key to PLAYER, so those bookings cannot carry a payment row. The paid flag
/// therefore lives on the entry type itself; walk-ins for a registered player still get a real
/// payment row on top, and that row wins when both are present.
/// </remarks>
public static class OwnerScheduleEntry
{
    public const string Blocked = "Blocked";

    /// <summary>Kept only so rows written before maintenance merged into Blocked still read.</summary>
    public const string Maintenance = "Maintenance";

    public const string Event = "Event";
    public const string WalkInPaid = "WalkIn";
    public const string WalkInUnpaid = "WalkInUnpaid";

    public static bool IsWalkIn(string? entryType) => entryType is WalkInPaid or WalkInUnpaid;

    public static bool LocksSlot(string? entryType) => entryType is Blocked or Maintenance;

    /// <summary>Payment status implied by the entry type, before any payment row is consulted.</summary>
    public static string? ImpliedPaymentStatus(string? entryType) => entryType switch
    {
        WalkInPaid => "Paid",
        WalkInUnpaid => "Pending",
        _ => null
    };
}
