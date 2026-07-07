using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/listing-fees")]
public class AdminListingFeesController : ControllerBase
{
    private readonly AdminListingFeeSettingService _settings;
    private readonly AdminListingFeePaymentService _payments;

    public AdminListingFeesController(
        AdminListingFeeSettingService settings,
        AdminListingFeePaymentService payments)
    {
        _settings = settings;
        _payments = payments;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ListingFeeSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        return Ok(await _settings.GetAsync(cancellationToken));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<ListingFeeSettingsResponse>> UpdateSettings(
        ListingFeeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _settings.UpdateAsync(request, CurrentUserId(), cancellationToken);
        return result.Status switch
        {
            ListingFeeSettingUpdateResultStatus.Success => Ok(result.Setting),
            ListingFeeSettingUpdateResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpGet("payments")]
    public async Task<ActionResult<PaginatedResponse<AdminListingFeePaymentResponse>>> GetPayments(
        string? status,
        string? search,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _payments.ListAsync(status, search, page, pageSize, cancellationToken);
        return result.IsInvalidStatus
            ? BadRequest(new { message = result.ErrorMessage })
            : Ok(result.Payments);
    }

    [HttpPost("payments/{paymentId:int}/confirm")]
    public async Task<ActionResult<AdminListingFeePaymentResponse>> ConfirmPayment(
        int paymentId,
        CancellationToken cancellationToken)
    {
        var result = await _payments.ConfirmAsync(paymentId, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("payments/{paymentId:int}/reject")]
    public async Task<ActionResult<AdminListingFeePaymentResponse>> RejectPayment(
        int paymentId,
        ListingFeePaymentRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.RejectAsync(paymentId, request, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<AdminListingFeePaymentResponse> ToActionResult(AdminListingFeePaymentReviewResult result) =>
        result.Status switch
        {
            AdminListingFeePaymentReviewResultStatus.Success => Ok(result.Payment),
            AdminListingFeePaymentReviewResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminListingFeePaymentReviewResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            AdminListingFeePaymentReviewResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}