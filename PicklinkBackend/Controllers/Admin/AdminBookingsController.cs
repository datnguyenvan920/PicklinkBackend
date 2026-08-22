using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/bookings")]
public class AdminBookingsController : ControllerBase
{
    private readonly IAdminBookingService _bookingService;

    public AdminBookingsController(IAdminBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminBookingSummaryResponse>>> GetBookings(
        string? search,
        string? status,
        string? paymentStatus,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _bookingService.ListAsync(
            search,
            status,
            paymentStatus,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpPost("{bookingId:int}/cancel")]
    public async Task<ActionResult<AdminBookingSummaryResponse>> CancelBooking(
        int bookingId,
        AdminBookingCancelRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null) return Unauthorized();

        var result = await _bookingService.CancelAsync(bookingId, request.Reason, actorId.Value, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{bookingId:int}/refund/dispute/resolve")]
    public async Task<ActionResult<AdminBookingSummaryResponse>> ResolveRefundDispute(
        int bookingId,
        AdminBookingRefundDisputeResolveRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null) return Unauthorized();

        return ToActionResult(await _bookingService.ResolveRefundDisputeAsync(
            bookingId, request.Resolution, actorId.Value, cancellationToken));
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private ActionResult<AdminBookingSummaryResponse> ToActionResult(AdminBookingCancelResult result) =>
        result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminResultStatus.Unauthorized => Unauthorized(new { message = result.ErrorMessage }),
            AdminResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            AdminResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
}
