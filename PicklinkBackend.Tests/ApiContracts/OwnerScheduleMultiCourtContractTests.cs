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
}