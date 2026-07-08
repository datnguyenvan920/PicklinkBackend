using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly PaymentService _paymentService;

    public PaymentController(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("bank-account")]
    public async Task<ActionResult<OwnerBankAccountResponse>> GetBankAccount(CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.GetBankAccount(cancellationToken));
    }

    [HttpPut("bank-account")]
    public async Task<ActionResult<OwnerBankAccountResponse>> UpsertBankAccount(
        OwnerBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.UpsertBankAccount(request, cancellationToken));
    }

    [HttpPost("bookings/{bookingId:int}/batch-preview")]
    public async Task<ActionResult<BatchPaymentPreviewResponse>> PreviewBatchTransfer(
        int bookingId,
        BatchPaymentPreviewRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.PreviewBatchTransfer(bookingId, request, cancellationToken));
    }

    [HttpPost("bookings/{bookingId:int}/submit-batch")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
    public async Task<ActionResult<BatchPaymentResponse>> SubmitBatchTransfer(
        int bookingId,
        [FromForm] SubmitBatchPaymentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.SubmitBatchTransfer(bookingId, request, cancellationToken));
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
        SetCurrentUser();
        return ToActionResult(await _paymentService.SubmitTransfer(bookingId, request, cancellationToken));
    }

    [HttpGet("operator")]
    public async Task<ActionResult<PaginatedResponse<BankTransferResponse>>> GetOperatorPayments(
        string status = "WaitingForConfirmation",
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.GetOperatorPayments(status, page, pageSize, cancellationToken));
    }

    [HttpGet("operator/{paymentId:int}")]
    public async Task<ActionResult<BankTransferResponse>> GetOperatorPayment(int paymentId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.GetOperatorPayment(paymentId, cancellationToken));
    }

    [HttpGet("operator/booking/{bookingId:int}")]
    public async Task<ActionResult<List<BankTransferResponse>>> GetOperatorBookingPayments(
        int bookingId,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.GetOperatorBookingPayments(bookingId, cancellationToken));
    }

    [HttpPost("operator/{paymentId:int}/approve")]
    public async Task<ActionResult<BankTransferResponse>> ApprovePayment(int paymentId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.ApprovePayment(paymentId, cancellationToken));
    }

    [HttpPost("operator/{paymentId:int}/reject")]
    public async Task<ActionResult<BankTransferResponse>> RejectPayment(
        int paymentId,
        RejectPaymentRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _paymentService.RejectPayment(paymentId, request, cancellationToken));
    }

    private void SetCurrentUser() =>
        _paymentService.SetCurrentUserId(CurrentUserId());

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private ActionResult<T> ToActionResult<T>(PaymentServiceResult<T> result) =>
        result.Status switch
        {
            PaymentServiceResultStatus.Success => Ok(result.Value),
            PaymentServiceResultStatus.NoContent => NoContent(),
            PaymentServiceResultStatus.BadRequest => BadRequest(result.Error),
            PaymentServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            PaymentServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            PaymentServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            PaymentServiceResultStatus.Conflict => Conflict(result.Error),
            PaymentServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };

    private IActionResult ToActionResult(PaymentServiceResult result) =>
        result.Status switch
        {
            PaymentServiceResultStatus.Success => result.Value is null ? Ok() : Ok(result.Value),
            PaymentServiceResultStatus.NoContent => NoContent(),
            PaymentServiceResultStatus.BadRequest => BadRequest(result.Error),
            PaymentServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            PaymentServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            PaymentServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            PaymentServiceResultStatus.Conflict => Conflict(result.Error),
            PaymentServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };
}