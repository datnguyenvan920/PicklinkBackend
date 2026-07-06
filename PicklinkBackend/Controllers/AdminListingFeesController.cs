using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/listing-fees")]
public class AdminListingFeesController : ControllerBase
{
    private static readonly string[] PaymentStatuses = ["PendingReview", "Confirmed", "Rejected"];
    private readonly ApplicationDbContext _dbContext;

    public AdminListingFeesController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ListingFeeSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        var setting = await LatestSetting(cancellationToken);
        return Ok(MapSetting(setting));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<ListingFeeSettingsResponse>> UpdateSettings(
        ListingFeeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PricePerCourtPerMonth <= 0 || request.PricePerCourtPerMonth > 100_000_000)
            return BadRequest(new { message = "Đơn giá phải lớn hơn 0 và không vượt quá 100.000.000đ." });

        var setting = new ListingFeeSetting
        {
            PricePerCourtPerMonth = request.PricePerCourtPerMonth,
            UpdatedAt = DateTime.UtcNow,
            UpdatedByUserId = CurrentUserId()
        };
        _dbContext.ListingFeeSettings.Add(setting);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapSetting(setting));
    }

    [HttpGet("payments")]
    public async Task<ActionResult<PaginatedResponse<AdminListingFeePaymentResponse>>> GetPayments(
        string? status,
        string? search,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var normalizedStatus = NormalizeStatus(status);
        if (!string.IsNullOrWhiteSpace(status)
            && !status.Equals("all", StringComparison.OrdinalIgnoreCase)
            && normalizedStatus is null)
        {
            return BadRequest(new { message = "Trạng thái phí lên sàn không hợp lệ." });
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

        return Ok(Pagination.Create(items, totalCount, page, pageSize));
    }

    [HttpPost("payments/{paymentId:int}/confirm")]
    public async Task<ActionResult<AdminListingFeePaymentResponse>> ConfirmPayment(
        int paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await LoadPayment(paymentId, cancellationToken);
        if (payment is null) return NotFound(new { message = "Không tìm thấy giao dịch phí lên sàn." });
        if (payment.Status != "PendingReview")
            return Conflict(new { message = "Chỉ có thể xác nhận giao dịch đang chờ duyệt." });

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
        payment.ReviewedByUserId = CurrentUserId();
        payment.PaidFrom = paidFrom;
        payment.PaidUntil = paidFrom.AddMonths(payment.Months);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapPayment(payment));
    }

    [HttpPost("payments/{paymentId:int}/reject")]
    public async Task<ActionResult<AdminListingFeePaymentResponse>> RejectPayment(
        int paymentId,
        ListingFeePaymentRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 3)
            return BadRequest(new { message = "Vui lòng nhập lý do từ chối ít nhất 3 ký tự." });

        var payment = await LoadPayment(paymentId, cancellationToken);
        if (payment is null) return NotFound(new { message = "Không tìm thấy giao dịch phí lên sàn." });
        if (payment.Status != "PendingReview")
            return Conflict(new { message = "Chỉ có thể từ chối giao dịch đang chờ duyệt." });

        payment.Status = "Rejected";
        payment.RejectionReason = reason;
        payment.ReviewedAt = DateTime.UtcNow;
        payment.ReviewedByUserId = CurrentUserId();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapPayment(payment));
    }

    private async Task<ListingFeeSetting?> LatestSetting(CancellationToken cancellationToken) =>
        await _dbContext.ListingFeeSettings.AsNoTracking()
            .OrderByDescending(setting => setting.UpdatedAt)
            .ThenByDescending(setting => setting.ListingFeeSettingId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<VenueListingPayment?> LoadPayment(int paymentId, CancellationToken cancellationToken) =>
        await _dbContext.VenueListingPayments
            .Include(payment => payment.Venue).ThenInclude(venue => venue.Owner).ThenInclude(owner => owner.User)
            .SingleOrDefaultAsync(payment => payment.VenueListingPaymentId == paymentId, cancellationToken);

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;

        return PaymentStatuses.FirstOrDefault(item => item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static ListingFeeSettingsResponse MapSetting(ListingFeeSetting? setting) => new()
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

public sealed class ListingFeeSettingsRequest
{
    public decimal PricePerCourtPerMonth { get; set; }
}

public sealed class ListingFeeSettingsResponse
{
    public int ListingFeeSettingId { get; set; }
    public decimal PricePerCourtPerMonth { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class ListingFeePaymentRejectionRequest
{
    public string? Reason { get; set; }
}

public sealed class AdminListingFeePaymentResponse
{
    public int VenueListingPaymentId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public int Months { get; set; }
    public int ActiveCourtCount { get; set; }
    public decimal PricePerCourtPerMonth { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReceiptImageUrl { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? PaidFrom { get; set; }
    public DateTime? PaidUntil { get; set; }
}
