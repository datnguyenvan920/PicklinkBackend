using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PlayerBookingDateLimitContractTests
{
    [Fact]
    public void PlayerBookingHoldRejectsDatesMoreThanOneMonthAhead()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));
        var createHolding = ExtractMethod(source, "CreateHolding", "GetMyBookings");

        Assert.Contains("var bookingDate = DateOnly.FromDateTime(DateTime.Now)", createHolding);
        Assert.Contains("var maxBookingDate = bookingDate.AddMonths(1)", createHolding);
        Assert.Contains("request.Date > maxBookingDate", createHolding);
        Assert.Contains("return BadRequest", createHolding);
        Assert.Contains("slot.Date > maxBookingDate", createHolding);
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


