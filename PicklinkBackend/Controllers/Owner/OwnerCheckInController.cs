using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Staff;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner/check-in")]
public sealed class OwnerCheckInController : ControllerBase
{
    private readonly StaffOperationService _operations;

    public OwnerCheckInController(StaffOperationService operations)
    {
        _operations = operations;
    }

    [HttpGet("bookings/today")]
    public async Task<ActionResult<PaginatedResponse<StaffBookingResponse>>> GetTodayBookings(
        DateOnly? date,
        string? bookingType,
        int? venueId,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _operations.ListTodayBookingsAsync(
            CurrentUserId(), date, bookingType, venueId, page, pageSize, cancellationToken));

    [HttpGet("bookings/search")]
    public async Task<ActionResult<StaffBookingResponse>> SearchBooking(
        string code,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.SearchBookingAsync(CurrentUserId(), code, cancellationToken));

    [HttpGet("bookings/{bookingId:int}")]
    public async Task<ActionResult<StaffBookingResponse>> GetBooking(
        int bookingId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.GetBookingAsync(CurrentUserId(), bookingId, cancellationToken));

    [HttpPost("bookings/verify-code")]
    public async Task<ActionResult<StaffBookingResponse>> VerifyBookingCodeByCode(
        VerifyBookingCodeRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.VerifyBookingCodeByCodeAsync(CurrentUserId(), request, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/confirm-at-court-payment")]
    public async Task<ActionResult<StaffBookingResponse>> ConfirmAtCourtPayment(
        int bookingId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.ConfirmAtCourtPaymentAsync(CurrentUserId(), bookingId, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/check-in")]
    public async Task<ActionResult<StaffBookingResponse>> CheckIn(
        int bookingId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.CheckInAsync(CurrentUserId(), bookingId, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/check-in-groups/{checkInGroupId:int}/check-in")]
    public async Task<ActionResult<StaffBookingResponse>> CheckInGroup(
        int bookingId,
        int checkInGroupId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.CheckInGroupAsync(CurrentUserId(), bookingId, checkInGroupId, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkNoShow(
        int bookingId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.MarkNoShowAsync(CurrentUserId(), bookingId, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/check-in-groups/{checkInGroupId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkGroupNoShow(
        int bookingId,
        int checkInGroupId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.MarkGroupNoShowAsync(CurrentUserId(), bookingId, checkInGroupId, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/participants/{playerId:int}/check-in")]
    public async Task<ActionResult<StaffBookingResponse>> CheckInMatchParticipant(
        int bookingId,
        int playerId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.CheckInMatchParticipantAsync(
            CurrentUserId(), bookingId, playerId, cancellationToken));

    [HttpPost("bookings/{bookingId:int}/participants/{playerId:int}/no-show")]
    public async Task<ActionResult<StaffBookingResponse>> MarkMatchParticipantNoShow(
        int bookingId,
        int playerId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _operations.MarkMatchParticipantNoShowAsync(
            CurrentUserId(), bookingId, playerId, cancellationToken));

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