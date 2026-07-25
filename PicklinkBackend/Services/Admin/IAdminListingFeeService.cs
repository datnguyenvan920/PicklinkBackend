using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Admin;

public interface IAdminListingFeeService
{
    Task<ListingFeeSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken);
    
    Task<ListingFeeSettingUpdateResult> UpdateSettingsAsync(
        ListingFeeSettingsRequest request,
        int? currentUserId,
        CancellationToken cancellationToken);

    Task<AdminListingFeePaymentListResult> ListPaymentsAsync(
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdminListingFeePaymentReviewResult> ConfirmPaymentAsync(
        int paymentId,
        int? reviewerUserId,
        CancellationToken cancellationToken);

    Task<AdminListingFeePaymentReviewResult> RejectPaymentAsync(
        int paymentId,
        ListingFeePaymentRejectionRequest request,
        int? reviewerUserId,
        CancellationToken cancellationToken);
}
