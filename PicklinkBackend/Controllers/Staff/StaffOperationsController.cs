using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Staff;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Staff")]
[Route("api/staff")]
public class StaffOperationsController : ControllerBase
{
    private readonly StaffOperationService _operations;

    public StaffOperationsController(StaffOperationService operations)
    {
        _operations = operations;
    }

    [HttpGet("assignments")]
    public async Task<ActionResult<List<StaffAssignmentResponse>>> GetAssignments(CancellationToken cancellationToken)
    {
        var result = await _operations.ListAssignmentsAsync(CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("bookings/today")]
    public async Task<ActionResult<PaginatedResponse<StaffBookingResponse>>> GetTodayBookings(
        DateOnly? date,
        string? bookingType,
        int? venueId,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _operations.ListTodayBookingsAsync(
            CurrentUserId(), date, bookingType, venueId, page, pageSize, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("bookings/search")]
    public async Task<ActionResult<StaffBookingResponse>> SearchBooking(
        string code,
        CancellationToken cancellationToken)
    {
        var result = await _operations.SearchBookingAsync(CurrentUserId(), code, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("bookings/{bookingId:int}")]
    public async Task<ActionResult<StaffBookingResponse>> GetBooking(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _operations.GetBookingAsync(CurrentUserId(), bookingId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("bookings/verify-code")]
    public async Task<ActionResult<StaffBookingResponse>> VerifyBookingCodeByCode(
        VerifyBookingCodeRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.VerifyBookingCodeByCodeAsync(CurrentUserId(), request, cancellationToken));


    [HttpPost("bookings/{bookingId:int}/confirm-at-court-payment")]
    public async Task<ActionResult<StaffBookingResponse>> ConfirmAtCourtPayment(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _operations.ConfirmAtCourtPaymentAsync(CurrentUserId(), bookingId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("bookings/{bookingId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkNoShow(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _operations.MarkNoShowAsync(CurrentUserId(), bookingId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("bookings/{bookingId:int}/check-in-groups/{checkInGroupId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkGroupNoShow(int bookingId, int checkInGroupId, CancellationToken cancellationToken) =>
        ToActionResult(await _operations.MarkGroupNoShowAsync(CurrentUserId(), bookingId, checkInGroupId, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/participants/{playerId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkMatchParticipantNoShow(
        int bookingId,
        int playerId,
        CancellationToken cancellationToken)
    {
        var result = await _operations.MarkMatchParticipantNoShowAsync(
            CurrentUserId(), bookingId, playerId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<List<StaffNotificationResponse>>> GetNotifications(CancellationToken cancellationToken)
    {
        var result = await _operations.ListNotificationsAsync(CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(StaffOperationResult<T> result) =>
        result.Status switch
        {
            StaffOperationResultStatus.Success => Ok(result.Value),
            StaffOperationResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            StaffOperationResultStatus.Unauthorized => Unauthorized(),
            StaffOperationResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            StaffOperationResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(500)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
