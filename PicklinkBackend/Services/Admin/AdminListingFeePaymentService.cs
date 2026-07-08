using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Admin;

public sealed class AdminListingFeePaymentService
{
    private static readonly string[] PaymentStatuses = ["PendingReview", "Confirmed", "Rejected"];
    private readonly ApplicationDbContext _dbContext;

    public AdminListingFeePaymentService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminListingFeePaymentListResult> ListAsync(
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
        var query = _dbContext.VenueListingPayments.AsNoTracking();
        if (normalizedStatus is not null)
            query = query.Where(payment => payment.Status == normalizedStatus);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(payment =>
                payment.Venue.VenueName.Contains(keyword)
                || payment.Venue.Owner.User.Username.Contains(keyword)
                || payment.Venue.Owner.User.Email.Contains(keyword));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(payment => payment.Status == "PendingReview")
            .ThenByDescending(payment => payment.SubmittedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(payment => new AdminListingFeePaymentResponse
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
            })
            .ToListAsync(cancellationToken);

        return AdminListingFeePaymentListResult.Success(Pagination.Create(items, totalCount, page, pageSize));
    }

    public async Task<AdminListingFeePaymentReviewResult> ConfirmAsync(
        int paymentId,
        int? reviewerUserId,
        CancellationToken cancellationToken)
    {
        var payment = await LoadPayment(paymentId, cancellationToken);
        if (payment is null) return AdminListingFeePaymentReviewResult.NotFound("Khong tim thay giao dich phi len san.");
        if (payment.Status != "PendingReview")
        {
            return AdminListingFeePaymentReviewResult.Conflict("Chi co the xac nhan giao dich dang cho duyet.");
        }

        var now = DateTime.UtcNow;
        var latestPaidUntil = await _dbContext.VenueListingPayments.AsNoTracking()
            .Where(item => item.VenueId == payment.VenueId
                && item.Status == "Confirmed"
                && item.PaidUntil != null)
            .MaxAsync(item => (DateTime?)item.PaidUntil, cancellationToken);
        var paidFrom = latestPaidUntil.HasValue && latestPaidUntil.Value > now
            ? latestPaidUntil.Value
            : now;

        payment.Status = "Confirmed";
        payment.RejectionReason = null;
        payment.ReviewedAt = now;
        payment.ReviewedByUserId = reviewerUserId;
        payment.PaidFrom = paidFrom;
        payment.PaidUntil = paidFrom.AddMonths(payment.Months);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return AdminListingFeePaymentReviewResult.Success(Map(payment));
    }

    public async Task<AdminListingFeePaymentReviewResult> RejectAsync(
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

        var payment = await LoadPayment(paymentId, cancellationToken);
        if (payment is null) return AdminListingFeePaymentReviewResult.NotFound("Khong tim thay giao dich phi len san.");
        if (payment.Status != "PendingReview")
        {
            return AdminListingFeePaymentReviewResult.Conflict("Chi co the tu choi giao dich dang cho duyet.");
        }

        payment.Status = "Rejected";
        payment.RejectionReason = reason;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewedByUserId = reviewerUserId;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return AdminListingFeePaymentReviewResult.Success(Map(payment));
    }

    private async Task<VenueListingPayment?> LoadPayment(int paymentId, CancellationToken cancellationToken) =>
        await _dbContext.VenueListingPayments
            .Include(payment => payment.Venue).ThenInclude(venue => venue.Owner).ThenInclude(owner => owner.User)
            .SingleOrDefaultAsync(payment => payment.VenueListingPaymentId == paymentId, cancellationToken);

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;

        return PaymentStatuses.FirstOrDefault(item => item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static AdminListingFeePaymentResponse Map(VenueListingPayment payment) => new()
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

public sealed record AdminListingFeePaymentListResult(
    PaginatedResponse<AdminListingFeePaymentResponse>? Payments,
    string? ErrorMessage)
{
    public bool IsInvalidStatus => ErrorMessage is not null;

    public static AdminListingFeePaymentListResult Success(PaginatedResponse<AdminListingFeePaymentResponse> payments) =>
        new(payments, ErrorMessage: null);

    public static AdminListingFeePaymentListResult InvalidStatus(string errorMessage) =>
        new(Payments: null, errorMessage);
}

public sealed record AdminListingFeePaymentReviewResult(
    AdminListingFeePaymentReviewResultStatus Status,
    AdminListingFeePaymentResponse? Payment,
    string? ErrorMessage)
{
    public static AdminListingFeePaymentReviewResult Success(AdminListingFeePaymentResponse payment) =>
        new(AdminListingFeePaymentReviewResultStatus.Success, payment, ErrorMessage: null);

    public static AdminListingFeePaymentReviewResult BadRequest(string errorMessage) =>
        new(AdminListingFeePaymentReviewResultStatus.BadRequest, Payment: null, errorMessage);

    public static AdminListingFeePaymentReviewResult NotFound(string errorMessage) =>
        new(AdminListingFeePaymentReviewResultStatus.NotFound, Payment: null, errorMessage);

    public static AdminListingFeePaymentReviewResult Conflict(string errorMessage) =>
        new(AdminListingFeePaymentReviewResultStatus.Conflict, Payment: null, errorMessage);
}

public enum AdminListingFeePaymentReviewResultStatus
{
    Success,
    BadRequest,
    NotFound,
    Conflict
}