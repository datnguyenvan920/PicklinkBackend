using Xunit;

namespace PicklinkBackend.Tests.ApiContracts;

public class OwnerScheduleMultiCourtContractTests
{
    [Fact]
    public void OwnerScheduleUsesChildSlotsForCourtOverlap()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PicklinkBackend", "Services", "Owner", "OwnerVenueService.cs"));

        Assert.Contains("booking.Slots.Any(slot => slot.CourtId == court.CourtId", source);
        Assert.Contains("!booking.Slots.Any() && booking.CourtId == court.CourtId", source);
    }

    [Fact]
    public void OwnerScheduleLoadsPaymentsByBookingIdsWithoutCollectionInclude()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PicklinkBackend", "Services", "Owner", "OwnerVenueService.cs"));
        var start = source.IndexOf("public async Task<ServiceResult<OwnerScheduleResponse>> GetScheduleV2", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<ServiceResult<OwnerScheduleResponse>> GetSchedule(", start, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.DoesNotContain(".Include(booking => booking.Payments)", method);
        Assert.Contains("bookingIds.Contains(payment.BookingId)", method);
        Assert.Contains("latestPayments.GetValueOrDefault(booking.BookingId)", method);
    }
    [Fact]
    public void OwnerScheduleShowsWholeBookingAmountAndSlotCheckInState()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var source = File.ReadAllText(Path.Combine(root, "PicklinkBackend", "Services", "Owner", "OwnerVenueService.cs"));
        var dto = File.ReadAllText(Path.Combine(root, "PicklinkBackend", "DTOs", "OwnerVenueDtos.cs"));

        Assert.Contains("Amount = booking.TotalAmount", source);
        Assert.Contains("GetSlotCheckInStatus(overlap, court.CourtId, slotStart, slotEnd, localNow)", source);
        Assert.Contains("public string? CheckInStatus { get; set; }", dto);
        Assert.Contains("public bool CanCancel { get; set; }", dto);
        Assert.Contains("public int? CustomerUserId { get; set; }", dto);
    }

    [Fact]
    public void OwnerCannotCancelAStartedOrPastSlot()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PicklinkBackend", "Services", "Owner", "OwnerVenueService.cs"));
        var start = source.IndexOf("public async Task<ServiceResult> UpdateBookingStatus", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool HasStartedSlot", start, StringComparison.Ordinal);
        var method = source[start..end];

        Assert.Contains(".Include(item => item.CheckInGroups)", method);
        Assert.Contains("request.Status == \"Cancelled\" && HasStartedSlot(booking, VietnamTime.Now)", method);
        Assert.Contains(".Include(item => item.Slots)", method);
        Assert.Contains("booking.Slots.Any(slot => localNow >= slot.StartTime)", source);
    }
}
