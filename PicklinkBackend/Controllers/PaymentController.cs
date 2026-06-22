using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentController : ControllerBase
{
    private static readonly HashSet<string> AllowedReceiptTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PaymentRealtimeNotifier _paymentRealtime;
    private readonly MatchRealtimeNotifier _matchRealtime;

    public PaymentController(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        ScheduleRealtimeNotifier scheduleRealtime,
        PaymentRealtimeNotifier paymentRealtime,
        MatchRealtimeNotifier matchRealtime)
    {
        _dbContext = dbContext;
        _environment = environment;
        _scheduleRealtime = scheduleRealtime;
        _paymentRealtime = paymentRealtime;
        _matchRealtime = matchRealtime;
    }

    [HttpGet("bank-account")]
    public async Task<ActionResult<OwnerBankAccountResponse>> GetBankAccount(CancellationToken cancellationToken)
    {
        var owner = await CurrentOwnerAsync(cancellationToken);
        if (owner is null) return Forbid();
        var account = await _dbContext.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == owner.OwnerId, cancellationToken);
        return account is null ? NotFound(new { message = "Chủ sân chưa cấu hình tài khoản nhận tiền." }) : Ok(MapAccount(account));
    }

    [HttpPut("bank-account")]
    public async Task<ActionResult<OwnerBankAccountResponse>> UpsertBankAccount(
        OwnerBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var owner = await CurrentOwnerAsync(cancellationToken);
        if (owner is null) return Forbid();
        var account = await _dbContext.OwnerBankAccounts.SingleOrDefaultAsync(item => item.OwnerId == owner.OwnerId, cancellationToken);
        if (account is null)
        {
            account = new OwnerBankAccount { OwnerId = owner.OwnerId, CreatedAt = DateTime.UtcNow };
            _dbContext.OwnerBankAccounts.Add(account);
        }

        account.BankCode = request.BankCode.Trim().ToUpperInvariant();
        account.BankName = request.BankName.Trim();
        account.AccountNumber = request.AccountNumber.Trim();
        account.AccountHolderName = request.AccountHolderName.Trim().ToUpperInvariant();
        account.IsActive = true;
        account.UpdatedAt = DateTime.UtcNow;
        foreach (var venueId in await _dbContext.Venues.Where(item => item.OwnerId == owner.OwnerId).Select(item => item.VenueId).ToListAsync(cancellationToken))
            _dbContext.VenueAuditLogs.Add(NewAudit(venueId, "BankAccountUpdated"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapAccount(account));
    }

    [HttpPost("bookings/{bookingId:int}/submit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
    public async Task<ActionResult<BankTransferResponse>> SubmitTransfer(
        int bookingId,
        [FromForm] SubmitPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        var receipt = request.Receipt;
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (receipt is null || receipt.Length == 0) return BadRequest(new { message = "Vui lòng tải ảnh biên lai." });
        if (receipt.Length > 5 * 1024 * 1024) return BadRequest(new { message = "Ảnh biên lai không được vượt quá 5 MB." });
        if (!AllowedReceiptTypes.Contains(receipt.ContentType)) return BadRequest(new { message = "Biên lai chỉ hỗ trợ JPG, PNG hoặc WEBP." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var payment = await PaymentQuery()
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.Payer.UserId == userId, cancellationToken);
        if (payment?.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đang được cập nhật. Vui lòng thử lại." });
        if (payment is null) return NotFound(new { message = "Không tìm thấy yêu cầu thanh toán." });
        if (payment.Booking.Status != "Holding") return Conflict(new { message = $"Không thể thanh toán booking {payment.Booking.Status}." });
        if (payment.Booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            Expire(payment, userId.Value, "Hết thời gian giữ chỗ trước khi gửi biên lai");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Conflict(new { message = "Thời gian giữ chỗ đã hết." });
        }
        if (payment.Status == "WaitingForConfirmation") return Ok(MapPayment(payment));
        if (payment.Status != "Pending") return Conflict(new { message = $"Thanh toán đang ở trạng thái {payment.Status}." });
        if (string.IsNullOrWhiteSpace(payment.QrImageUrl)) return Conflict(new { message = "Chủ sân chưa cấu hình tài khoản nhận tiền." });

        var receiptUrl = await SaveReceiptAsync(payment.PaymentId, receipt, cancellationToken);
        var previous = payment.Status;
        payment.ReceiptImageUrl = receiptUrl;
        payment.Status = "WaitingForConfirmation";
        payment.SubmittedAt = DateTime.UtcNow;
        payment.RejectionReason = null;
        payment.StatusHistories.Add(NewHistory(previous, payment.Status, "Submitted", "Player xác nhận đã chuyển khoản", userId));
        _dbContext.VenueAuditLogs.Add(NewAudit(payment.Booking.Court.VenueId, $"PaymentSubmitted:{payment.PaymentId}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            payment.Booking.Court.VenueId, payment.Booking.CourtId, payment.Booking.StartTime, payment.Booking.EndTime, "Holding", "Updated"));
        PublishPaymentChanged(payment, "Submitted");
        return Ok(MapPayment(payment));
    }

    [HttpGet("operator")]
    public async Task<ActionResult<List<BankTransferResponse>>> GetOperatorPayments(
        string status = "WaitingForConfirmation",
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var query = AuthorizedOperatorQuery(userId.Value);
        if (!status.Equals("All", StringComparison.OrdinalIgnoreCase)) query = query.Where(item => item.Status == status);
        var payments = await query
            .OrderByDescending(item => item.SubmittedAt)
            .ThenByDescending(item => item.PaymentId)
            .ToListAsync(cancellationToken);
        return Ok(payments.Select(MapPayment).ToList());
    }

    [HttpGet("operator/{paymentId:int}")]
    public async Task<ActionResult<BankTransferResponse>> GetOperatorPayment(int paymentId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var payment = await AuthorizedOperatorQuery(userId.Value).SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        return payment is null ? NotFound(new { message = "Không tìm thấy thanh toán trong sân được phân quyền." }) : Ok(MapPayment(payment));
    }

    [HttpPost("operator/{paymentId:int}/approve")]
    public async Task<ActionResult<BankTransferResponse>> ApprovePayment(int paymentId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"payment-review:{paymentId}", cancellationToken))
            return Conflict(new { message = "Thanh toán đang được xử lý." });
        var payment = await AuthorizedOperatorQuery(userId.Value).SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        if (payment?.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đang được cập nhật. Vui lòng thử lại." });
        if (payment is null) return NotFound(new { message = "Không tìm thấy thanh toán trong sân được phân quyền." });
        if (payment.Status == "Paid") return Ok(MapPayment(payment));
        if (payment.Status != "WaitingForConfirmation") return Conflict(new { message = "Chỉ có thể xác nhận thanh toán đang chờ duyệt." });
        if (payment.Booking.Status is "Cancelled" or "Expired") return Conflict(new { message = "Không thể xác nhận booking đã hủy hoặc hết hạn." });
        if (payment.Booking.Status != "Holding" || payment.Booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            Expire(payment, userId.Value, "Hết thời gian giữ chỗ trước khi chủ sân xác nhận");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Conflict(new { message = "Booking đã hết thời gian giữ chỗ." });
        }

        payment.Status = "Paid";
        payment.PaidAt = DateTime.UtcNow;
        payment.VerifiedAt = DateTime.UtcNow;
        payment.VerifiedByUserId = userId;
        payment.RejectionReason = null;
        payment.StatusHistories.Add(NewHistory("WaitingForConfirmation", "Paid", "Approved", "Owner/Staff xác nhận giao dịch", userId));
        FinalizeBookingAfterPaymentApproval(payment);
        if (payment.Booking.Status == "Confirmed") payment.Booking.HoldExpiresAt = null;
        if (payment.Booking.Status == "Confirmed") payment.Booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = "Holding", ToStatus = "Confirmed", Reason = "Thanh toán chuyển khoản đã được xác nhận",
            ActorUserId = userId, ChangedAt = DateTime.UtcNow
        });
        _dbContext.VenueAuditLogs.Add(NewAudit(payment.Booking.Court.VenueId, $"PaymentApproved:{payment.PaymentId}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            payment.Booking.Court.VenueId, payment.Booking.CourtId, payment.Booking.StartTime, payment.Booking.EndTime, payment.Booking.Status, "Updated"));
        PublishPaymentChanged(payment, "Approved");
        return Ok(MapPayment(payment));
    }

    [HttpPost("operator/{paymentId:int}/reject")]
    public async Task<ActionResult<BankTransferResponse>> RejectPayment(
        int paymentId,
        RejectPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"payment-review:{paymentId}", cancellationToken))
            return Conflict(new { message = "Thanh toán đang được xử lý." });
        var payment = await AuthorizedOperatorQuery(userId.Value).SingleOrDefaultAsync(item => item.PaymentId == paymentId, cancellationToken);
        if (payment?.Booking.MatchId is int matchId
            && !await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Trận đang được cập nhật. Vui lòng thử lại." });
        if (payment is null) return NotFound(new { message = "Không tìm thấy thanh toán trong sân được phân quyền." });
        if (payment.Status != "WaitingForConfirmation") return Conflict(new { message = "Chỉ có thể từ chối thanh toán đang chờ duyệt." });
        if (payment.Booking.Status is "Cancelled" or "Expired") return Conflict(new { message = "Booking đã hủy hoặc hết hạn." });

        payment.Status = "Pending";
        payment.RejectionReason = request.Reason.Trim();
        payment.VerifiedAt = DateTime.UtcNow;
        payment.VerifiedByUserId = userId;
        payment.StatusHistories.Add(NewHistory("WaitingForConfirmation", "Pending", "Rejected", payment.RejectionReason, userId));
        _dbContext.VenueAuditLogs.Add(NewAudit(payment.Booking.Court.VenueId, $"PaymentRejected:{payment.PaymentId}:{payment.RejectionReason}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishPaymentChanged(payment, "Rejected");
        return Ok(MapPayment(payment));
    }

    private IQueryable<Payment> PaymentQuery() => _dbContext.Payments
        .Include(item => item.StatusHistories)
        .Include(item => item.Payer).ThenInclude(item => item.User)
        .Include(item => item.Booking).ThenInclude(item => item.StatusHistories)
        .Include(item => item.Booking).ThenInclude(item => item.Payments)
        .Include(item => item.Booking).ThenInclude(item => item.Match).ThenInclude(item => item!.MatchParticipants)
        .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue);

    private static void FinalizeBookingAfterPaymentApproval(Payment payment)
    {
        var match = payment.Booking.Match;
        if (match is null)
        {
            payment.Booking.Status = "Confirmed";
            return;
        }

        var acceptedPlayerIds = match.MatchParticipants
            .Where(item => item.Status == "Accepted")
            .Select(item => item.PlayerId)
            .ToHashSet();
        var paidPlayerIds = payment.Booking.Payments
            .Where(item => item.Status == "Paid")
            .Select(item => item.PayerId)
            .ToHashSet();
        var canConfirm = acceptedPlayerIds.Count == match.RequiredPlayerCount
            && acceptedPlayerIds.All(paidPlayerIds.Contains);

        match.Status = canConfirm ? "Confirmed" : "PaymentPending";
        payment.Booking.Status = canConfirm ? "Confirmed" : "Holding";
    }

    private IQueryable<Payment> AuthorizedOperatorQuery(int userId) => PaymentQuery()
        .Where(item => item.Booking.Court.Venue.Owner.UserId == userId || item.Booking.Court.Venue.Staff.Any(staff =>
            staff.UserId == userId && staff.IsActive && staff.Permissions.Contains("ConfirmPayment")));

    private async Task<VenueOwner?> CurrentOwnerAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return userId is null ? null : await _dbContext.VenueOwners.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
    }

    private async Task<string> SaveReceiptAsync(int paymentId, IFormFile receipt, CancellationToken cancellationToken)
    {
        var extension = receipt.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"payment-{paymentId}-{Guid.NewGuid():N}{extension}";
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(root, "uploads", "payment-receipts");
        Directory.CreateDirectory(directory);
        await using var stream = System.IO.File.Create(Path.Combine(directory, fileName));
        await receipt.CopyToAsync(stream, cancellationToken);
        return $"{Request.Scheme}://{Request.Host}/uploads/payment-receipts/{fileName}";
    }

    private void Expire(Payment payment, int actorUserId, string reason)
    {
        foreach (var item in payment.Booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var previousPayment = item.Status;
            item.Status = "Expired";
            item.StatusHistories.Add(NewHistory(previousPayment, "Expired", "BookingExpired", reason, actorUserId));
        }
        var previousBooking = payment.Booking.Status;
        payment.Booking.Status = "Expired";
        payment.Booking.HoldExpiresAt = null;
        if (payment.Booking.Match is not null)
        {
            payment.Booking.Match.Status = "Cancelled";
            payment.Booking.Match.CancelledAt = DateTime.UtcNow;
        }
        payment.Booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = previousBooking, ToStatus = "Expired", Reason = reason, ActorUserId = actorUserId, ChangedAt = DateTime.UtcNow
        });
    }

    private VenueAuditLog NewAudit(int venueId, string action) => new()
    {
        VenueId = venueId, ActorId = CurrentUserId()!.Value, Action = action, Timestamp = DateTime.UtcNow
    };

    private void PublishPaymentChanged(Payment payment, string action)
    {
        _paymentRealtime.Publish(new PaymentChangedEvent(
            payment.PaymentId,
            payment.BookingId,
            payment.Booking.Court.VenueId,
            payment.Status,
            action));
        if (payment.Booking.MatchId.HasValue)
            _matchRealtime.Publish(payment.Booking.MatchId.Value, $"Payment{action}");
    }

    private static PaymentStatusHistory NewHistory(string? from, string to, string action, string? reason, int? actorUserId) => new()
    {
        FromStatus = from, ToStatus = to, Action = action, Reason = reason, ActorUserId = actorUserId, CreatedAt = DateTime.UtcNow
    };

    private static OwnerBankAccountResponse MapAccount(OwnerBankAccount item) => new()
    {
        OwnerBankAccountId = item.OwnerBankAccountId, BankCode = item.BankCode, BankName = item.BankName,
        AccountNumber = item.AccountNumber, AccountHolderName = item.AccountHolderName, IsActive = item.IsActive
    };

    private static BankTransferResponse MapPayment(Payment payment) => new()
    {
        PaymentId = payment.PaymentId,
        BookingId = payment.BookingId,
        BookingCode = payment.Booking.BookingCode ?? $"PL-{payment.BookingId}",
        BookingStatus = payment.Booking.Status,
        PaymentStatus = payment.Status,
        Amount = payment.Amount,
        TransferCode = payment.TransferCode,
        TransferContent = payment.TransferContent,
        BankCode = payment.BankCode,
        BankName = payment.BankName,
        BankAccountNumber = payment.BankAccountNumber,
        BankAccountName = payment.BankAccountName,
        QrImageUrl = payment.QrImageUrl,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        SubmittedAt = AsUtc(payment.SubmittedAt),
        VerifiedAt = AsUtc(payment.VerifiedAt),
        RejectionReason = payment.RejectionReason,
        HoldExpiresAt = AsUtc(payment.Booking.HoldExpiresAt),
        VenueId = payment.Booking.Court.VenueId,
        VenueName = payment.Booking.Court.Venue.VenueName,
        CourtNumber = payment.Booking.Court.CourtNumber,
        StartTime = payment.Booking.StartTime,
        EndTime = payment.Booking.EndTime,
        PlayerName = payment.Payer.User.Username,
        History = payment.StatusHistories.OrderBy(item => item.CreatedAt).Select(item => new PaymentHistoryResponse
        {
            FromStatus = item.FromStatus, ToStatus = item.ToStatus, Action = item.Action,
            Reason = item.Reason, CreatedAt = AsUtc(item.CreatedAt)
        }).ToList()
    };

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
