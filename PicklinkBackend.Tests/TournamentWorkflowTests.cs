using PicklinkBackend.Models;
using PicklinkBackend.Services;
using Xunit;

namespace PicklinkBackend.Tests;

public class TournamentWorkflowTests
{
    [Fact]
    public void EnsureCheckInCode_ApprovedAndPaid_GeneratesStableCode()
    {
        var registration = new TournamentRegistration
        {
            TournamentId = 8,
            TournamentRegistrationId = 42,
            Status = "Approved",
            PaymentStatus = "Confirmed"
        };

        TournamentWorkflow.EnsureCheckInCode(registration, () => "ABC12345");
        TournamentWorkflow.EnsureCheckInCode(registration, () => "DIFFERENT");

        Assert.Equal("PKL-T8-R42-ABC12345", registration.CheckInCode);
    }

    [Theory]
    [InlineData("Pending", "Confirmed")]
    [InlineData("Approved", "Pending")]
    [InlineData("Rejected", "Confirmed")]
    public void EnsureCheckInCode_IneligibleRegistration_DoesNotGenerate(
        string registrationStatus,
        string paymentStatus)
    {
        var registration = new TournamentRegistration
        {
            TournamentId = 1,
            TournamentRegistrationId = 2,
            Status = registrationStatus,
            PaymentStatus = paymentStatus
        };

        TournamentWorkflow.EnsureCheckInCode(registration, () => "ABC12345");

        Assert.Null(registration.CheckInCode);
    }

    [Theory]
    [InlineData("Draft", "PendingApproval", true)]
    [InlineData("PendingApproval", "Open", true)]
    [InlineData("Open", "Completed", false)]
    [InlineData("Completed", "Open", false)]
    public void TournamentStatusTransition_FollowsWorkflow(
        string current,
        string next,
        bool expected)
    {
        Assert.Equal(expected, TournamentWorkflow.CanTransitionTournament(current, next));
    }

    [Fact]
    public void Slugify_RemovesVietnameseDiacritics()
    {
        Assert.Equal(
            "giai-pickleball-mua-he-2026",
            TournamentWorkflow.Slugify("Giải Pickleball Mùa Hè 2026"));
    }
}
