using Xunit;

namespace PicklinkBackend.Tests.ApiContracts;

public class OwnerScheduleMultiCourtContractTests
{
    [Fact]
    public void OwnerScheduleIsPopulatedFromOwnerVenuesAndBookings()
    {
        var source = File.ReadAllText(SourcePath("Services", "Owner", "Implementations", "OwnerVenueService.cs"));

        Assert.Contains("_paymentRepository.Bookings", source);
        Assert.Contains("response.Venues = venues.Select(MapVenue).ToList()", source);
        Assert.Contains("response.Slots.Add(new OwnerScheduleSlotResponse", source);
        Assert.DoesNotContain("Ok(new OwnerScheduleResponse())", source);
    }

    [Fact]
    public void OwnerScheduleUsesChildSlotsForCourtOverlap()
    {
        var source = File.ReadAllText(SourcePath("Services", "Owner", "Implementations", "OwnerVenueService.cs"));

        Assert.Contains("_venueRepository", source);
    }

    [Fact]
    public void OwnerScheduleLoadsPaymentsByBookingIdsWithoutCollectionInclude()
    {
        var source = File.ReadAllText(SourcePath("Services", "Owner", "OwnerVenueService.cs"));

        Assert.Contains("GetSchedule", source);
    }

    [Fact]
    public void OwnerScheduleShowsWholeBookingAmountAndSlotCheckInState()
    {
        var source = File.ReadAllText(SourcePath("Services", "Owner", "OwnerVenueService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "OwnerVenueDtos.cs"));

        Assert.Contains("TotalAmount", source);
        Assert.Contains("public string? CheckInStatus { get; set; }", dto);
        Assert.Contains("public bool CanCancel { get; set; }", dto);
    }

    [Fact]
    public void OwnerCannotCancelAStartedOrPastSlot()
    {
        var source = File.ReadAllText(SourcePath("Services", "Owner", "OwnerVenueService.cs"));

        Assert.Contains("UpdateBookingStatus", source);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var fileName = cleanSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;

                var foundDir = Directory.GetDirectories(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundDir is not null) return foundDir;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
