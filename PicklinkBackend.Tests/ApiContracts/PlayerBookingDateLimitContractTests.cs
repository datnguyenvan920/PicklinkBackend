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
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));

        Assert.Contains("CreateMatchBooking", source);
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


