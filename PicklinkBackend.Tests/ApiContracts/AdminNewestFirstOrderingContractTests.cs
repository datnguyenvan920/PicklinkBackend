namespace PicklinkBackend.Tests;

public class AdminNewestFirstOrderingContractTests
{
    private readonly string _repository = File.ReadAllText(
        SourcePath("Repositories", "Implementations", "AdminRepository.cs"));

    [Theory]
    [InlineData("GetAdminUserListAsync", ".OrderByDescending(user => user.UserId)")]
    [InlineData("GetAdminBookingListAsync", ".OrderByDescending(booking => booking.CreatedAt)", ".ThenByDescending(booking => booking.BookingId)")]
    [InlineData("GetAdminListingFeePaymentListAsync", ".OrderByDescending(payment => payment.SubmittedAt)", ".ThenByDescending(payment => payment.VenueListingPaymentId)")]
    [InlineData("GetAdminReportListAsync", ".OrderByDescending(report => report.CreatedAt)", ".ThenByDescending(report => report.CommunityReportId)")]
    [InlineData("GetAdminReviewListAsync", ".OrderByDescending(review => review.CreatedAt)", ".ThenByDescending(review => review.RatingId)")]
    [InlineData("GetAdminPostListAsync", ".OrderByDescending(post => post.CreatedAt)", ".ThenByDescending(post => post.PostId)")]
    [InlineData("GetAdminClubListAsync", ".OrderByDescending(group => group.CreatedAt)", ".ThenByDescending(group => group.GroupId)")]
    public void AdminPagedListsOrderNewestFirst(
        string methodName,
        string primaryOrder,
        string? stableOrder = null)
    {
        var method = ExtractMethod(_repository, methodName);

        Assert.Contains(primaryOrder, method);
        if (stableOrder is not null)
            Assert.Contains(stableOrder, method);
    }

    [Fact]
    public void AdminVenueListOrdersByLatestSubmissionThenNewestId()
    {
        var method = ExtractMethod(_repository, "GetAdminVenueListAsync");
        var submittedOrder = method.IndexOf(
            ".OrderByDescending(venue => venue.VenueAuditLogs",
            StringComparison.Ordinal);
        var idOrder = method.IndexOf(
            ".ThenByDescending(venue => venue.VenueId)",
            StringComparison.Ordinal);

        Assert.True(submittedOrder >= 0);
        Assert.True(idOrder > submittedOrder);
        Assert.DoesNotContain(".OrderByDescending(venue => venue.ApprovalStatus", method);
    }

    [Fact]
    public void AdminDashboardActionQueueOrdersNewestEventsFirst()
    {
        var method = ExtractMethod(_repository, "GetAdminDashboardAsync");
        var actionItems = method.IndexOf("ActionItems = actionItems", StringComparison.Ordinal);
        var createdOrder = method.IndexOf(
            ".OrderByDescending(item => item.CreatedAt)",
            actionItems,
            StringComparison.Ordinal);
        var toneOrder = method.IndexOf(
            ".ThenByDescending(item => item.Tone",
            actionItems,
            StringComparison.Ordinal);

        Assert.True(actionItems >= 0);
        Assert.True(createdOrder > actionItems);
        Assert.True(toneOrder > createdOrder);
    }

    [Fact]
    public void AdminDashboardExpiringListingsOrderByNewestPayment()
    {
        var method = ExtractMethod(_repository, "GetAdminDashboardAsync");
        var expiringListings = method.IndexOf(
            "expiringListings = await",
            StringComparison.Ordinal);
        var submittedOrder = method.IndexOf(
            ".OrderByDescending(venue => venue.VenueListingPayments",
            expiringListings,
            StringComparison.Ordinal);
        var idOrder = method.IndexOf(
            ".ThenByDescending(venue => venue.VenueId)",
            expiringListings,
            StringComparison.Ordinal);

        Assert.True(expiringListings >= 0);
        Assert.True(submittedOrder > expiringListings);
        Assert.True(idOrder > submittedOrder);
    }

    [Fact]
    public void SettingsAndNotificationsKeepTheirExistingNewestFirstOrder()
    {
        var settings = ExtractMethod(_repository, "GetLatestListingFeeSettingAsync");
        var notifications = File.ReadAllText(
            SourcePath("Services", "Notifications", "Implementations", "NotificationQueryService.cs"));

        Assert.Contains(".OrderByDescending(setting => setting.UpdatedAt)", settings);
        Assert.Contains(".ThenByDescending(setting => setting.ListingFeeSettingId)", settings);
        Assert.Contains(".OrderByDescending(notification => notification.CreatedAt)", notifications);
        Assert.Contains(".ThenByDescending(notification => notification.NotifId)", notifications);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var start = source.IndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {methodName}.");

        var nextMethod = source.IndexOf("\n    public ", start + methodName.Length, StringComparison.Ordinal);
        return nextMethod >= 0 ? source[start..nextMethod] : source[start..];
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. relativeSegments]);
                if (File.Exists(candidate)) return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
