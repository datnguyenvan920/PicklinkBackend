using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;

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
        if (request.PricePerCourtPerMonth <= 0 || request.PricePerCourtPerMonth > 100_000_000)
        {
            return ListingFeeSettingUpdateResult.BadRequest("Don gia phai lon hon 0 va khong vuot qua 100.000.000d.");
        }

        var setting = new ListingFeeSetting
        {
            PricePerCourtPerMonth = request.PricePerCourtPerMonth,
            UpdatedAt = DateTime.UtcNow,
            UpdatedByUserId = currentUserId
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
            return AdminListingFeePaymentListResult.InvalidStatus("Trang thai phi len san khong hop le.");
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
        var payment = await _adminRepository.GetVenueListingPaymentByIdAsync(paymentId, cancellationToken);
        if (payment is null) return AdminListingFeePaymentReviewResult.NotFound("Khong tim thay giao dich phi len san.");
        if (payment.Status != "PendingReview")
        {
            return AdminListingFeePaymentReviewResult.Conflict("Chi co the xac nhan giao dich dang cho duyet.");
        }

        var now = DateTime.UtcNow;
        var latestPaidUntil = await _adminRepository.GetLatestPaidUntilByVenueIdAsync(payment.VenueId, cancellationToken);
        var paidFrom = latestPaidUntil.HasValue && latestPaidUntil.Value > now
            ? latestPaidUntil.Value
            : now;

        payment.Status = "Confirmed";
        payment.RejectionReason = null;
        payment.ReviewedAt = now;
        payment.ReviewedByUserId = reviewerUserId;
        payment.PaidFrom = paidFrom;
        payment.PaidUntil = paidFrom.AddMonths(payment.Months);

        await _adminRepository.SaveChangesAsync(cancellationToken);
        return AdminListingFeePaymentReviewResult.Success(MapPayment(payment));
    }

    public async Task<AdminListingFeePaymentReviewResult> RejectPaymentAsync(
        int paymentId,
        ListingFeePaymentRejectionRequest request,
        int? reviewerUserId,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 3)
        {
            return AdminListingFeePaymentReviewResult.BadRequest("Vui long nhap ly do tu choi it nhat 3 ky tu.");
        }

        var payment = await _adminRepository.GetVenueListingPaymentByIdAsync(paymentId, cancellationToken);
        if (payment is null) return AdminListingFeePaymentReviewResult.NotFound("Khong tim thay giao dich phi len san.");
        if (payment.Status != "PendingReview")
        {
            return AdminListingFeePaymentReviewResult.Conflict("Chi co the tu choi giao dich dang cho duyet.");
        }

        payment.Status = "Rejected";
        payment.RejectionReason = reason;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewedByUserId = reviewerUserId;

        await _adminRepository.SaveChangesAsync(cancellationToken);
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
