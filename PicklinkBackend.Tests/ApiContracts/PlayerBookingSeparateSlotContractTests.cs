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
        Assert.Contains("_bookingRepository.AddAsync(booking", createHolding);
        Assert.Contains("return Ok(response)", createHolding);
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
    public void PlayerBookingHoldMapsResponseBeforeCommitUsingLoadedVenue()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        const string mapResponse = "var response = MapBooking(booking, parentCourt, venue)";
        const string commit = "await transaction.CommitAsync";
        Assert.Contains(mapResponse, createHolding);
        Assert.True(createHolding.IndexOf(mapResponse, StringComparison.Ordinal)
            < createHolding.IndexOf(commit, StringComparison.Ordinal));
        Assert.Contains("var venue = venueOverride ?? court.Venue", source);
        Assert.DoesNotContain("court.Venue.OpenTime", createHolding);
    }

    [Fact]
    public void PlayerBookingHoldKeepsEachSelectedSlotsDate()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        Assert.Contains("Start = slot.Date.ToDateTime(slot.StartTime)", createHolding);
        Assert.Contains("End = slot.Date.ToDateTime(slot.StartTime).AddMinutes(30)", createHolding);
        Assert.DoesNotContain("request.Date.ToDateTime(slot.StartTime)", createHolding);
    }
    [Fact]
    public void PlayerBookingHoldRequestsScheduleConflictConfirmation()
    {
        var dto = File.ReadAllText(SourcePath("DTOs", "PlayerBookingDtos.cs"));
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        Assert.Contains("public bool AllowScheduleConflicts { get; set; }", dto);
        Assert.Contains("if (!request.AllowScheduleConflicts)", createHolding);
        Assert.Contains("LoadConflictDetailsAsync(", createHolding);
        Assert.Contains("requiresScheduleConflictConfirmation = true", createHolding);
    }
    [Fact]
    public void ScheduleConflictDetailsUseActualBookingSlotsInsteadOfBookingEnvelope()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerScheduleConflictService.cs"));

        Assert.Contains("_bookingRepository.LoadConflictDetailsAsync", source);
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
