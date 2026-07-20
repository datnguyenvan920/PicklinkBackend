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
        var payment = File.ReadAllText(SourcePath("Services", "Payments", "PaymentService.cs"));

        Assert.Contains("Include(item => item.Booking).ThenInclude(item => item.Slots)", payment);
        Assert.Contains("foreach (var slot in booking.Slots)", payment);
        Assert.Contains("PublishScheduleChanged(payment.Booking", payment);
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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
