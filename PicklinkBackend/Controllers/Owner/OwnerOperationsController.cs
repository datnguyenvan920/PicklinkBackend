using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Owner;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner")]
public class OwnerOperationsController : ControllerBase
{
    private readonly OwnerOperationQueryService _operations;

    public OwnerOperationsController(OwnerOperationQueryService operations)
    {
        _operations = operations;
    }

    [HttpGet("bookings")]
    public async Task<ActionResult<PaginatedResponse<OwnerBookingResponse>>> GetBookings(
        DateOnly? from,
        DateOnly? to,
        string? status,
        string? search,
        string? bookingType,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _operations.ListBookingsAsync(
            from,
            to,
            status,
            search,
            bookingType,
            page,
            pageSize,
            CurrentUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("bookings/{bookingId:int}")]
    public async Task<ActionResult<OwnerBookingResponse>> GetBooking(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _operations.GetBookingAsync(bookingId, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("reports/revenue")]
    public async Task<ActionResult<OwnerRevenueReportResponse>> GetRevenueReport(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await _operations.GetRevenueReportAsync(from, to, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(OwnerOperationResult<T> result) =>
        result.Status switch
        {
            OwnerOperationResultStatus.Success => Ok(result.Value),
            OwnerOperationResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            OwnerOperationResultStatus.Unauthorized => Unauthorized(),
            OwnerOperationResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}