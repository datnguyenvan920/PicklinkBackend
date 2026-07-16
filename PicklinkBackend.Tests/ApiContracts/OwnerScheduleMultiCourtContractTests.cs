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
}