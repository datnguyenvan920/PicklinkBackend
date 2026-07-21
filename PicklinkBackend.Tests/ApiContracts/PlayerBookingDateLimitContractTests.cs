using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PlayerBookingDateLimitContractTests
{
    [Fact]
    public void PlayerBookingHoldRejectsDatesMoreThanTwelveMonthsAhead()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        Assert.Contains("var bookingDate = DateOnly.FromDateTime(VietnamTime.Now)", createHolding);
        Assert.Contains("private const int MaximumAdvanceBookingMonths = 12", source);
        Assert.Contains("var maxBookingDate = bookingDate.AddMonths(MaximumAdvanceBookingMonths)", createHolding);
        Assert.Contains("request.Date > maxBookingDate", createHolding);
        Assert.Contains("return BadRequest", createHolding);
        Assert.Contains("slot.Date > maxBookingDate", createHolding);
    }

    [Fact]
    public void MatchBookingRejectsDatesMoreThanTwelveMonthsAhead()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "MatchService.Open.cs"));
        var createBooking = ExtractMethod(source, "CreateMatchBooking", "CancelPendingMatchBooking");

        Assert.Contains("private const int MaximumAdvanceBookingMonths = 12", source);
        Assert.Contains("DateOnly.FromDateTime(VietnamTime.Now).AddMonths(MaximumAdvanceBookingMonths)", createBooking);
        Assert.Contains("DateOnly.FromDateTime(slot.StartTime) > maxBookingDate", createBooking);
        Assert.Contains("return BadRequest", createBooking);
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


