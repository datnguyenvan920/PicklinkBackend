using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PlayerBookingDateLimitContractTests
{
    [Fact]
    public void PlayerBookingHoldOnlyAllowsCurrentAndNextCalendarMonth()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        Assert.Contains("var bookingDate = DateOnly.FromDateTime(VietnamTime.Now)", createHolding);
        Assert.Contains("private const int MaximumAdvanceBookingMonths = 1", source);
        Assert.Contains("new DateOnly(bookingDate.Year, bookingDate.Month, 1)", createHolding);
        Assert.Contains(".AddMonths(MaximumAdvanceBookingMonths + 1)", createHolding);
        Assert.Contains(".AddDays(-1)", createHolding);
        Assert.Contains("request.Date > maxBookingDate", createHolding);
        Assert.Contains("return BadRequest", createHolding);
        Assert.Contains("slot.Date > maxBookingDate", createHolding);
    }

    [Fact]
    public void MatchBookingOnlyAllowsCurrentAndNextCalendarMonth()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.cs"));
        var openSource = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));
        var createBooking = ExtractMethod(openSource, "CreateMatchBooking", "CancelPendingMatchBooking");

        Assert.Contains("private const int MaximumAdvanceBookingMonths = 1", source);
        Assert.Contains("new DateOnly(bookingDate.Year, bookingDate.Month, 1)", createBooking);
        Assert.Contains(".AddMonths(MaximumAdvanceBookingMonths + 1)", createBooking);
        Assert.Contains(".AddDays(-1)", createBooking);
        Assert.Contains("DateOnly.FromDateTime(slot.StartTime) > maxBookingDate", createBooking);
        Assert.Contains("return BadRequest", createBooking);
    }

    [Fact]
    public void TicketSessionBookingOnlyAllowsCurrentAndNextCalendarMonth()
    {
        var source = File.ReadAllText(SourcePath("Services", "Ticketing", "Implementations", "TicketingService.cs"));

        Assert.Contains("new DateOnly(today.Year, today.Month, 1)", source);
        Assert.Contains(".AddMonths(MaximumAdvanceBookingMonths + 1)", source);
        Assert.Contains("date > maxBookingDate", source);
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


