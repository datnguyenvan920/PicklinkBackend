using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Admin.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/listing-fees")]
public class AdminListingFeesController : ControllerBase
{
    private readonly IAdminListingFeeService _listingFeeService;

    public AdminListingFeesController(IAdminListingFeeService listingFeeService)
    {
        _listingFeeService = listingFeeService;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ListingFeeSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        return Ok(await _listingFeeService.GetSettingsAsync(cancellationToken));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<ListingFeeSettingsResponse>> UpdateSettings(
        ListingFeeSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _listingFeeService.UpdateSettingsAsync(request, CurrentUserId(), cancellationToken);
        return result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminResultStatus.Unauthorized => Unauthorized(),
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
        var result = await _listingFeeService.ListPaymentsAsync(status, search, page, pageSize, cancellationToken);
        return !result.IsSuccess
            ? BadRequest(new { message = result.ErrorMessage })
            : Ok(result.Value);
    }

    [HttpPost("payments/{paymentId:int}/confirm")]
    public async Task<ActionResult<AdminListingFeePaymentResponse>> ConfirmPayment(
        int paymentId,
        CancellationToken cancellationToken)
    {
        var result = await _listingFeeService.ConfirmPaymentAsync(paymentId, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("payments/{paymentId:int}/reject")]
    public async Task<ActionResult<AdminListingFeePaymentResponse>> RejectPayment(
        int paymentId,
        ListingFeePaymentRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _listingFeeService.RejectPaymentAsync(paymentId, request, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<AdminListingFeePaymentResponse> ToActionResult(AdminListingFeePaymentReviewResult result) =>
        result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminResultStatus.Unauthorized => Unauthorized(),
            AdminResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            AdminResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
