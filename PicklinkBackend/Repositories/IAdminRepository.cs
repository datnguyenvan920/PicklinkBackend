using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Repositories;

public interface IAdminRepository
{
    Task<(List<AdminUserSummaryResponse> Items, int TotalCount)> GetAdminUserListAsync(
        string? keyword,
        string? normalizedRole,
        bool lockedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<User?> GetUserForLockByIdAsync(int userId, CancellationToken cancellationToken = default);

    Task<(List<AdminVenueSummaryResponse> Items, int TotalCount)> GetAdminVenueListAsync(
        string? keyword,
        string? normalizedStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Venue?> GetAdminVenueDetailAsync(int venueId, CancellationToken cancellationToken = default);

    Task<Venue?> GetVenueForApprovalByIdAsync(int venueId, CancellationToken cancellationToken = default);

    Task<(List<AdminBookingSummaryResponse> Items, int TotalCount)> GetAdminBookingListAsync(
        string? keyword,
        string? normalizedStatus,
        string? normalizedPaymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AdminDashboardResponse> GetAdminDashboardAsync(CancellationToken cancellationToken = default);

    Task<(List<AdminListingFeePaymentResponse> Items, int TotalCount)> GetAdminListingFeePaymentListAsync(
        string? normalizedStatus,
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<VenueListingPayment?> GetVenueListingPaymentByIdAsync(int paymentId, CancellationToken cancellationToken = default);

    Task<DateTime?> GetLatestPaidUntilByVenueIdAsync(int venueId, CancellationToken cancellationToken = default);

    Task<ListingFeeSetting?> GetLatestListingFeeSettingAsync(CancellationToken cancellationToken = default);

    Task AddListingFeeSettingAsync(ListingFeeSetting setting, CancellationToken cancellationToken = default);

    Task<Dictionary<string, PlatformSetting>> GetPlatformSettingsAsync(CancellationToken cancellationToken = default);

    Task<PlatformSetting?> GetPlatformSettingByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task AddPlatformSettingAsync(PlatformSetting setting, CancellationToken cancellationToken = default);

    Task<(List<AdminReportResponse> Items, int TotalCount)> GetAdminReportListAsync(
        string? keyword,
        string? normalizedStatus,
        string? normalizedTargetType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CommunityReport?> GetCommunityReportByIdAsync(int reportId, CancellationToken cancellationToken = default);

    Task<(List<AdminReviewResponse> Items, int TotalCount)> GetAdminReviewListAsync(
        string? keyword,
        string? normalizedStatus,
        string? normalizedTargetType,
        int? score,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<RatingHistory?> GetRatingHistoryByIdAsync(int ratingId, CancellationToken cancellationToken = default);

    Task<(List<AdminPostResponse> Items, int TotalCount)> GetAdminPostListAsync(
        string? keyword,
        bool? hiddenOnly,
        int? groupId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Post?> GetPostForModerationByIdAsync(int postId, CancellationToken cancellationToken = default);

    Task RemovePostAsync(Post post, CancellationToken cancellationToken = default);

    Task<(List<AdminClubResponse> Items, int TotalCount)> GetAdminClubListAsync(
        string? keyword,
        bool? suspendedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<SocialGroup?> GetGroupForModerationByIdAsync(int groupId, CancellationToken cancellationToken = default);

    Task<Booking?> GetBookingForCancelByIdAsync(int bookingId, CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.Serializable,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
