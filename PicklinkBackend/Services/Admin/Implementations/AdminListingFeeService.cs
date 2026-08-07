using System.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Bookings;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminListingFeeService : IAdminListingFeeService
{
    private static readonly string[] PaymentStatuses = ["PendingReview", "Confirmed", "Rejected"];
    private readonly IAdminRepository _adminRepository;

    public AdminListingFeeService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    public async Task<ListingFeeSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
    {
        return MapSetting(await _adminRepository.GetLatestListingFeeSettingAsync(cancellationToken));
    }

    public async Task<ListingFeeSettingUpdateResult> UpdateSettingsAsync(
        ListingFeeSettingsRequest request,
        int? currentUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId is null)
        {
            return ListingFeeSettingUpdateResult.Unauthorized();
        }

        if (request.PricePerCourtPerMonth <= 0 || request.PricePerCourtPerMonth > 100_000_000)
        {
            return ListingFeeSettingUpdateResult.BadRequest("Đơn giá phải lớn hơn 0 và không vượt quá 100.000.000đ.");
        }

        var setting = new ListingFeeSetting
        {
            PricePerCourtPerMonth = request.PricePerCourtPerMonth,
            UpdatedAt = DateTime.UtcNow,
            UpdatedByUserId = currentUserId.Value
        };
        await _adminRepository.AddListingFeeSettingAsync(setting, cancellationToken);
        await _adminRepository.SaveChangesAsync(cancellationToken);

        return ListingFeeSettingUpdateResult.Success(MapSetting(setting));
    }

    public async Task<AdminListingFeePaymentListResult> ListPaymentsAsync(
        string? status,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var normalizedStatus = NormalizeStatus(status);
        if (!string.IsNullOrWhiteSpace(status)
            && !status.Equals("all", StringComparison.OrdinalIgnoreCase)
            && normalizedStatus is null)
        {
            return AdminListingFeePaymentListResult.InvalidStatus("Trạng thái phí lên sàn không hợp lệ.");
        }

        var keyword = search?.Trim();
        var (items, totalCount) = await _adminRepository.GetAdminListingFeePaymentListAsync(
            normalizedStatus, keyword, page, pageSize, cancellationToken);

        return AdminListingFeePaymentListResult.Success(Pagination.Create(items, totalCount, page, pageSize));
    }

    public async Task<AdminListingFeePaymentReviewResult> ConfirmPaymentAsync(
        int paymentId,
        int? reviewerUserId,
        CancellationToken cancellationToken)
    {
        if (reviewerUserId is null)
        {
            return AdminListingFeePaymentReviewResult.Unauthorized();
        }

        await using var transaction = await _adminRepository.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction,
                $"admin-listing-payment:{paymentId}",
                cancellationToken))
        {
            return AdminListingFeePaymentReviewResult.Conflict(
                "Giao dịch đang được xử lý. Vui lòng thử lại.");
        }

        var payment = await _adminRepository.GetVenueListingPaymentByIdAsync(paymentId, cancellationToken);
        if (payment is null) return AdminListingFeePaymentReviewResult.NotFound("Không tìm thấy giao dịch phí lên sàn.");
        if (payment.Status != "PendingReview")
        {
            return AdminListingFeePaymentReviewResult.Conflict("Chỉ có thể xác nhận giao dịch đang chờ duyệt.");
        }

        if (!await SqlServerBookingLock.AcquireAsync(
                transaction,
                $"admin-listing-venue:{payment.VenueId}",
                cancellationToken))
        {
            return AdminListingFeePaymentReviewResult.Conflict(
                "Phí lên sàn của sân đang được xử lý. Vui lòng thử lại.");
        }

        var now = DateTime.UtcNow;
        var latestPaidUntil = await _adminRepository.GetLatestPaidUntilByVenueIdAsync(payment.VenueId, cancellationToken);
        var paidFrom = latestPaidUntil.HasValue && latestPaidUntil.Value > now
            ? latestPaidUntil.Value
            : now;

        payment.Status = "Confirmed";
        payment.RejectionReason = null;
        payment.ReviewedAt = now;
        payment.ReviewedByUserId = reviewerUserId.Value;
        payment.PaidFrom = paidFrom;
        payment.PaidUntil = paidFrom.AddMonths(payment.Months);

        await _adminRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AdminListingFeePaymentReviewResult.Success(MapPayment(payment));
    }

    public async Task<AdminListingFeePaymentReviewResult> RejectPaymentAsync(
        int paymentId,
        ListingFeePaymentRejectionRequest request,
        int? reviewerUserId,
        CancellationToken cancellationToken)
    {
        if (reviewerUserId is null)
        {
            return AdminListingFeePaymentReviewResult.Unauthorized();
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length is < 3 or > 500)
        {
            return AdminListingFeePaymentReviewResult.BadRequest(
                "Lý do từ chối phải từ 3 đến 500 ký tự.");
        }

        await using var transaction = await _adminRepository.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction,
                $"admin-listing-payment:{paymentId}",
                cancellationToken))
        {
            return AdminListingFeePaymentReviewResult.Conflict(
                "Giao dịch đang được xử lý. Vui lòng thử lại.");
        }

        var payment = await _adminRepository.GetVenueListingPaymentByIdAsync(paymentId, cancellationToken);
        if (payment is null) return AdminListingFeePaymentReviewResult.NotFound("Không tìm thấy giao dịch phí lên sàn.");
        if (payment.Status != "PendingReview")
        {
            return AdminListingFeePaymentReviewResult.Conflict("Chỉ có thể từ chối giao dịch đang chờ duyệt.");
        }

        payment.Status = "Rejected";
        payment.RejectionReason = reason;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewedByUserId = reviewerUserId.Value;

        await _adminRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AdminListingFeePaymentReviewResult.Success(MapPayment(payment));
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;

        return PaymentStatuses.FirstOrDefault(item => item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static AdminListingFeeSettingResponse MapSetting(ListingFeeSetting? setting) => new()
    {
        ListingFeeSettingId = setting?.ListingFeeSettingId ?? 0,
        PricePerCourtPerMonth = setting?.PricePerCourtPerMonth ?? 0,
        UpdatedAt = setting?.UpdatedAt
    };

    private static AdminListingFeePaymentResponse MapPayment(VenueListingPayment payment) => new()
    {
        VenueListingPaymentId = payment.VenueListingPaymentId,
        VenueId = payment.VenueId,
        VenueName = payment.Venue.VenueName,
        OwnerName = payment.Venue.Owner.User.Username,
        OwnerEmail = payment.Venue.Owner.User.Email,
        Months = payment.Months,
        ActiveCourtCount = payment.ActiveCourtCount,
        PricePerCourtPerMonth = payment.PricePerCourtPerMonth,
        Amount = payment.Amount,
        Status = payment.Status,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        RejectionReason = payment.RejectionReason,
        SubmittedAt = payment.SubmittedAt,
        ReviewedAt = payment.ReviewedAt,
        PaidFrom = payment.PaidFrom,
        PaidUntil = payment.PaidUntil
    };
}
