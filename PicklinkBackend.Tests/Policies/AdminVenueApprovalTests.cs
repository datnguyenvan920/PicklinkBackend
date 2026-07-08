using PicklinkBackend.Models;
using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Tests;

public class AdminVenueApprovalTests
{
    [Fact]
    public void ApproveMovesPendingVenueToApprovedAndWritesAuditLog()
    {
        var now = new DateTime(2026, 7, 6, 10, 30, 0, DateTimeKind.Utc);
        var venue = new Venue
        {
            VenueId = 42,
            ApprovalStatus = "Pending",
            RejectionReason = "LÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â½ do cÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â©"
        };

        var actor = Admin(7);
        var error = VenueApprovalWorkflow.Approve(venue, actor, now);

        Assert.Null(error);
        Assert.Equal("Approved", venue.ApprovalStatus);
        Assert.Null(venue.RejectionReason);
        var audit = Assert.Single(venue.VenueAuditLogs);
        Assert.Equal(42, audit.VenueId);
        Assert.Equal(7, audit.ActorId);
        Assert.Same(actor, audit.Actor);
        Assert.Equal("AdminApprovedVenue", audit.Action);
        Assert.Equal(now, audit.Timestamp);
    }

    [Fact]
    public void RejectMovesPendingVenueToRejectedAndStoresTrimmedReason()
    {
        var now = new DateTime(2026, 7, 6, 11, 0, 0, DateTimeKind.Utc);
        var venue = new Venue
        {
            VenueId = 43,
            ApprovalStatus = "Pending"
        };

        var actor = Admin(8);
        var error = VenueApprovalWorkflow.Reject(
            venue,
            actor,
            reason: "  ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¢nh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ hiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§y ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n.  ",
            now);

        Assert.Null(error);
        Assert.Equal("Rejected", venue.ApprovalStatus);
        Assert.Equal("ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¢nh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ hiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§y ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n.", venue.RejectionReason);
        var audit = Assert.Single(venue.VenueAuditLogs);
        Assert.Equal("AdminRejectedVenue", audit.Action);
        Assert.Same(actor, audit.Actor);
        Assert.Equal(now, audit.Timestamp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public void RejectRequiresAReasonWithAtLeastThreeCharacters(string? reason)
    {
        var venue = new Venue
        {
            VenueId = 44,
            ApprovalStatus = "Pending"
        };

        var error = VenueApprovalWorkflow.Reject(
            venue,
            Admin(8),
            reason,
            DateTime.UtcNow);

        Assert.NotNull(error);
        Assert.Equal("Pending", venue.ApprovalStatus);
        Assert.Null(venue.RejectionReason);
        Assert.Empty(venue.VenueAuditLogs);
    }

    [Fact]
    public void ApprovalOnlyAcceptsPendingVenues()
    {
        var venue = new Venue
        {
            VenueId = 45,
            ApprovalStatus = "Draft"
        };

        var error = VenueApprovalWorkflow.Approve(venue, Admin(7), DateTime.UtcNow);

        Assert.NotNull(error);
        Assert.Equal("Draft", venue.ApprovalStatus);
        Assert.Empty(venue.VenueAuditLogs);
    }

    [Fact]
    public void RejectLimitsReasonToFiveHundredCharacters()
    {
        var venue = new Venue
        {
            VenueId = 46,
            ApprovalStatus = "Pending"
        };

        var error = VenueApprovalWorkflow.Reject(
            venue,
            Admin(8),
            reason: new string('a', 501),
            DateTime.UtcNow);

        Assert.NotNull(error);
        Assert.Equal("Pending", venue.ApprovalStatus);
        Assert.Empty(venue.VenueAuditLogs);
    }

    private static User Admin(int userId) => new()
    {
        UserId = userId,
        Username = $"admin-{userId}",
        Email = $"admin-{userId}@picklink.test",
        PasswordHash = "not-used",
        UserType = "Admin"
    };
}
