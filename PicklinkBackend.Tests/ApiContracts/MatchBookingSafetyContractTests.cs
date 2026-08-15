namespace PicklinkBackend.Tests;

public class MatchBookingSafetyContractTests
{
    [Fact]
    public void MatchBookingRevalidatesSlotsAndVenueOnTheServer()
    {
        var method = CreateMatchBookingSource();

        Assert.Contains("parsedSlots.Count > 496", method);
        Assert.Contains("DistinctBy(slot => new { slot.CourtId, slot.StartTime })", method);
        Assert.Contains("slot.EndTime != slot.StartTime.AddMinutes(30)", method);
        Assert.Contains("slot.StartTime <= VietnamTime.Now", method);
        Assert.Contains("DateOnly.FromDateTime(slot.EndTime) != DateOnly.FromDateTime(slot.StartTime)", method);
        Assert.Contains("courts.Select(court => court.VenueId).Distinct().Skip(1).Any()", method);
        Assert.Contains("PreferredVenueIds(match).Contains(venue.VenueId)", method);
        Assert.Contains("venue.ApprovalStatus != \"Approved\"", method);
        Assert.Contains("court.AvailabilityStatus != \"Available\"", method);
        Assert.Contains("TimeOnly.FromDateTime(slot.StartTime) < venue.OpenTime", method);
    }

    [Fact]
    public void MatchBookingSerializesPlayerAndCourtSchedulesBeforeOverlapChecks()
    {
        var method = CreateMatchBookingSource();

        Assert.Contains("match.Status is not (\"ReadyToBook\" or \"Booked\")", method);
        Assert.Contains("approvedParticipants.Count != match.RequiredPlayerCount", method);
        Assert.Contains("player-schedule:{participantId}", method);
        Assert.Contains("court-schedule:{slot.CourtId}:{slot.StartTime:yyyyMMdd}", method);
        Assert.Contains("OrderBy(resource => resource, StringComparer.Ordinal)", method);
        Assert.DoesNotContain("b.MatchId != matchId", method);
        Assert.True(method.IndexOf("player-schedule:{participantId}", StringComparison.Ordinal)
            < method.IndexOf("courtScheduleLocks", StringComparison.Ordinal));
        Assert.True(method.IndexOf("courtScheduleLocks", StringComparison.Ordinal)
            < method.IndexOf("overlappingBookings", StringComparison.Ordinal));
    }

    [Fact]
    public void BookedMatchCanCreateANonOverlappingFollowUpBookingImmediately()
    {
        var method = CreateMatchBookingSource();

        Assert.Contains("match.Status is not (\"ReadyToBook\" or \"Booked\")", method);
        Assert.DoesNotContain("hasActiveBooking", method);
        Assert.DoesNotContain("lượt đặt sân chưa kết thúc", method);
        Assert.Contains("if (overlaps)", method);
    }

    private static string CreateMatchBookingSource()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));
        var start = source.IndexOf(" CreateMatchBooking(", StringComparison.Ordinal);
        var end = source.IndexOf(" CancelPendingMatchBooking(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not locate CreateMatchBooking.");
        return source[start..end];
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var fileName = relativeSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. relativeSegments]);
                if (File.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
