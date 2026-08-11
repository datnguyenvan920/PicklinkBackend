namespace PicklinkBackend.Tests.ApiContracts;

public class PostMatchReviewApiContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void PlayerReviewsArePersistedAndProtectedByMatchMembershipRules()
    {
        var source = Read("PicklinkBackend", "Services", "Matches", "Implementations", "MatchService.Reviews.cs");
        var legacySource = Read("PicklinkBackend", "Services", "Matches", "Implementations", "MatchService.cs");

        Assert.Contains("match.Status != \"Completed\"", source);
        Assert.Contains("currentPlayerId.Value == revieweePlayerId", source);
        Assert.Contains("IsApprovedOrAccepted(item.Status)", source);
        Assert.Contains("match.MatchPlayerReviews.Add(review)", source);
        Assert.Contains("ReviewerPlayerId == currentPlayerId.Value", source);
        Assert.Contains("RevieweePlayerId == revieweePlayerId", source);
        Assert.Contains("RevieweePlayerId == currentPlayerId.Value", source);
        Assert.DoesNotContain("Ok<MatchPlayerReviewResponse>(new MatchPlayerReviewResponse())", legacySource);
    }

    [Fact]
    public void CompletedMatchAlsoCompletesBookingsAndPublishesRealtimeChange()
    {
        var matchmaking = Read("PicklinkBackend", "Services", "Matches", "Implementations", "MatchmakingService.cs");
        var worker = Read("PicklinkBackend", "Services", "Matches", "MatchmakingWorker.cs");

        Assert.Contains("booking.Status = \"Completed\"", matchmaking);
        Assert.Contains("Reason = \"MatchCompleted\"", matchmaking);
        Assert.Contains("_matchRealtime.Publish(completedMatchId, \"MatchCompleted\")", matchmaking);
        Assert.Contains("await RunMatchmakingScanAsync(stoppingToken);", worker);
        Assert.Contains("_scanGate.WaitAsync(0, cancellationToken)", worker);
    }

    [Fact]
    public void PaidMatchParticipantsCanReviewAnEndedVenueBooking()
    {
        var repository = Read("PicklinkBackend", "Repositories", "Implementations", "BookingRepository.cs");
        var service = Read("PicklinkBackend", "Services", "Bookings", "Implementations", "PlayerBookingReviewService.cs");

        Assert.Contains("item.Match!.MatchParticipants.Any", repository);
        Assert.Contains("payment.PayerId == participant.PlayerId && payment.Status == \"Paid\"", repository);
        Assert.Contains("booking.Status == \"Confirmed\" && booking.EndTime <= VietnamTime.Now", service);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot }.Concat(path).ToArray()));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "PicklinkBackend"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PicklinkBackend repository root.");
    }
}
