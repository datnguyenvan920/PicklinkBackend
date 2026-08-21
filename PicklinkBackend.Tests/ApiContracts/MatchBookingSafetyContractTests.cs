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

        Assert.Contains("operationalStatus is not (\"ReadyToBook\" or \"Booked\" or \"Completed\")", method);
        Assert.Contains("MatchRoomLifecyclePolicy.EffectiveRoomStatusFor", method);
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

    /// <summary>
    /// Replaces BookedMatchCanCreateANonOverlappingFollowUpBookingImmediately: a booked match
    /// used to be free to stack another round right away. The rule now is that a group plays
    /// the round it holds out before booking the next one, enforced by the shared gate.
    /// </summary>
    [Fact]
    public void BookedMatchWaitsForTheNextRoundGateInsteadOfStackingBookings()
    {
        var method = CreateMatchBookingSource();

        Assert.Contains("operationalStatus is not (\"ReadyToBook\" or \"Booked\" or \"Completed\")", method);
        Assert.Contains("EvaluateNextRoundGateAsync(matchId, cancellationToken)", method);
        Assert.Contains("if (overlaps)", method);
    }

    [Fact]
    public void CompletedMatchReopensForAnotherBookingAndKeepsReviewsOnPlayedRounds()
    {
        var method = CreateMatchBookingSource();

        Assert.Contains("operationalStatus is not (\"ReadyToBook\" or \"Booked\" or \"Completed\")", method);
        Assert.Contains("match.Status = \"BookingPending\"", method);
        Assert.DoesNotContain("match.Status != \"Completed\"", ReviewMatchPlayerSource());
    }

    [Fact]
    public void NextRoundIsGatedOnThePreviousRoundBeingPlayedOutOnly()
    {
        var gate = NextRoundGateSource();

        Assert.Contains("booking.EndTime > localNow", gate);
        Assert.Contains("Chỉ được đặt lượt tiếp theo sau khi lượt đã đặt chơi xong.", gate);
        Assert.Contains("if (!nextRoundGate.CanBook)", CreateMatchBookingSource());

        // Rating is encouraged but must never block the next booking.
        Assert.DoesNotContain("MatchPlayerReviews", gate);
        Assert.DoesNotContain("RatingHistories", gate);
    }

    [Fact]
    public void RatingNeedsAFinishedRoundAndAPersonalCheckIn()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Reviews.cs")).Replace("\r\n", "\n");
        var eligibility = source[source.IndexOf(" CheckReviewEligibilityAsync(\n        int matchId", StringComparison.Ordinal)..];

        Assert.Contains("item.Status == \"Confirmed\" && item.EndTime <= localNow", eligibility);
        Assert.Contains("Chỉ được đánh giá sau khi lượt chơi đã kết thúc.", eligibility);
        Assert.Contains("Chỉ người đã check-in tại sân mới được đánh giá.", eligibility);
        Assert.Contains("item.Status == \"Present\"", source);

        // Editing a score rebuilds prestige from the stored rows instead of folding it
        // onto the old average, which would drift further apart on every edit.
        Assert.Contains("UpdateMatchPlayerReview(", source);
        Assert.Contains("excludedReviewId == null || item.MatchPlayerReviewId != excludedReviewId.Value", source);

        var venueReviews = File.ReadAllText(
            SourcePath("Services", "Bookings", "Implementations", "PlayerBookingReviewService.cs"));

        Assert.Contains("Chỉ người đã check-in tại sân mới được đánh giá.", venueReviews);
        // A rating belongs to the venue, not to the round or the room.
        Assert.Contains("Bạn đã đánh giá sân này rồi, hãy sửa đánh giá cũ.", venueReviews);
        Assert.Contains("public async Task<PlayerBookingReviewResult> UpdateVenueAsync(", venueReviews);
        Assert.Contains("UpdateVenueOverallRatingAsync(venueId, cancellationToken)", venueReviews);
        // The scan must land on the code whose window is open, not on the match as a whole.
        Assert.Contains("checkIn.BookingCheckInGroup.BookingId == booking.BookingId", venueReviews);
    }

    [Fact]
    public void AttendanceIsRecordedPerCheckInCodeRatherThanPerMatch()
    {
        var staff = File.ReadAllText(SourcePath("Services", "Staff", "Implementations", "StaffOperationService.cs"));

        Assert.Contains("var scannedGroup = booking.CheckInGroups", staff);
        Assert.Contains("localNow >= item.StartTime.AddMinutes(-30) && localNow <= item.EndTime", staff);
        Assert.Contains("BookingCheckInGroupId = scannedGroup.BookingCheckInGroupId", staff);
        Assert.Contains("item.BookingCheckInGroupId == scannedGroup.BookingCheckInGroupId", staff);
        // A later round must not inherit the previous round's attendance.
        Assert.Contains("bookingGroupIds.Contains(item.BookingCheckInGroupId.Value)", staff);

        var schema = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("ALTER TABLE [MATCH_CHECKIN] ADD [bookingCheckInGroupId] int NULL;", schema);
        Assert.Contains("ON [MATCH_CHECKIN] ([matchId], [playerId], [bookingCheckInGroupId])", schema);
        // EF models a unique index over a nullable column as a filtered one; the raw SQL has to
        // match or every later "migrations add" re-emits the index as a pending change.
        Assert.Contains("WHERE [bookingCheckInGroupId] IS NOT NULL;", schema);

        var migration = File.ReadAllText(
            MigrationPath("20260816090000_AddMatchCheckInGroup.cs"));

        Assert.Contains("WHERE [bookingCheckInGroupId] IS NOT NULL;", migration);
        Assert.Contains("MatchCheckInGroupSql.Backfill", migration);
        Assert.Contains("MatchCheckInGroupSql.Backfill", schema);
    }

    private static string MigrationPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend", "Migrations", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate migration {fileName}.");
    }

    private static string NextRoundGateSource()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Open.cs"));
        var start = source.IndexOf(" EvaluateNextRoundGateAsync(", StringComparison.Ordinal);
        var end = source.IndexOf(" CreateMatchBooking(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not locate EvaluateNextRoundGateAsync.");
        return source[start..end];
    }

    private static string ReviewMatchPlayerSource()
    {
        var source = File.ReadAllText(SourcePath("Services", "Matches", "Implementations", "MatchService.Reviews.cs"));
        var start = source.IndexOf(" ReviewMatchPlayer(", StringComparison.Ordinal);
        var end = source.IndexOf(" GetMatchPlayerReviews(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not locate ReviewMatchPlayer.");
        return source[start..end];
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
