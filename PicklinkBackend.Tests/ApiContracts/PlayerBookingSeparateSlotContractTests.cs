using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PlayerBookingSeparateSlotContractTests
{
    [Fact]
    public void PlayerBookingHoldCreatesOneBookingForAllSelectedSlots()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        Assert.Contains("var selectedRanges = selectedSlots.Select", createHolding);
        Assert.Contains("var booking = new Booking", createHolding);
        Assert.Contains("booking.Slots.Add", createHolding);
        Assert.Contains("booking.CheckInGroups.Add", createHolding);
        Assert.Contains("_dbContext.Bookings.Add(booking)", createHolding);
        Assert.Contains("return Ok(MapBooking(booking", createHolding);
    }

    [Fact]
    public void PlayerBookingHoldChecksConflictsPerSelectedSlotOnly()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        Assert.Contains("selectedRanges.Where((slot, index) =>", createHolding);
        Assert.Contains("slot.Start < other.End && slot.End > other.Start", createHolding);
    }

    [Fact]
    public void PlayerBookingDurationSumsEachSelectedSlot()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));

        Assert.Contains("booking.Slots.Sum(slot => EF.Functions.DateDiffMinute(slot.StartTime, slot.EndTime))", source);
        Assert.Contains("booking.Slots.Sum(slot => (slot.EndTime - slot.StartTime).TotalHours)", source);
    }

    [Fact]
    public void PaidBookingCannotBeCancelled()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var cancelBooking = ExtractMethod(source, "CancelBooking", "RetryPayment");

        Assert.Contains("booking.Payments.Any(item => item.Status == \"Paid\")", cancelBooking);
        Assert.Contains("!booking.Payments.Any(item => item.Status == \"Paid\")", source);
    }

    private static string ExtractMethod(string source, string methodName, string nextMethodName)
    {
        var pattern = $"public .*? {methodName}\\([\\s\\S]*?\\n    public .*? {nextMethodName}\\(";
        var match = Regex.Match(source, pattern);
        Assert.True(match.Success, $"Could not locate {methodName}.");
        return match.Value;
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
