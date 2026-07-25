namespace PicklinkBackend.Tests;

public class SingleBookingSlotLifecycleContractTests
{
    [Fact]
    public void AvailabilityUsesChildSlotsForNewBookings()
    {
        var service = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));

        Assert.Contains("booking.Slots.Any(slot => slot.CourtId == court.CourtId", service);
        Assert.Contains("!booking.Slots.Any()", service);
    }

    [Fact]
    public void BookingChangePublishesEveryChildSlot()
    {
        var service = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var expiry = File.ReadAllText(SourcePath("Services", "Bookings", "BookingHoldExpirationService.cs"));

        Assert.Contains("foreach (var slot in booking.Slots)", service);
        Assert.Contains("foreach (var slot in booking.Slots)", expiry);
    }

    [Fact]
    public void PaymentLifecyclePublishesEveryChildSlot()
    {
        var payment = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("ScheduleRealtimeNotifier", payment);
    }

    [Fact]
    public void PlayerCheckInCodesOnlyOpenForEachReadyOccurrenceWindow()
    {
        var service = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));

        Assert.Contains("group.CheckInStatus == \"Ready\"", service);
        Assert.Contains("localNow >= group.StartTime.AddMinutes(-30)", service);
        Assert.Contains("localNow <= group.EndTime", service);
        Assert.Contains("item.CheckInStatus == \"Ready\"", service);
        Assert.Contains("VietnamTime.Now >= item.StartTime.AddMinutes(-30)", service);
        Assert.DoesNotContain("? item.CheckInCode : null", service);
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
