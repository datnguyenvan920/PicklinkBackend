using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/player-bookings")]
public class PlayerBookingController : ControllerBase
{
    private readonly PlayerBookingService _playerBookingService;

    public PlayerBookingController(PlayerBookingService playerBookingService)
    {
        _playerBookingService = playerBookingService;
    }

    [AllowAnonymous]
    [HttpGet("venues")]
    public async Task<ActionResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetVenues(
        string? search,
        string? area,
        double? minPrice,
        double? maxPrice,
        bool favoritesOnly = false,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.GetVenues(search, area, minPrice, maxPrice, favoritesOnly, page, pageSize, cancellationToken));
    }

    [Authorize]
    [HttpGet("favorites")]
    public async Task<ActionResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetFavoriteVenues(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.GetFavoriteVenues(page, pageSize, cancellationToken));
    }

    [Authorize]
    [HttpPut("favorites/{venueId:int}")]
    public async Task<IActionResult> AddFavoriteVenue(int venueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.AddFavoriteVenue(venueId, cancellationToken));
    }

    [Authorize]
    [HttpDelete("favorites/{venueId:int}")]
    public async Task<IActionResult> RemoveFavoriteVenue(int venueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.RemoveFavoriteVenue(venueId, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("venues/{venueId:int}/availability")]
    public async Task<ActionResult<PlayerCourtAvailabilityResponse>> GetAvailability(
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.GetAvailability(venueId, date, cancellationToken));
    }

    [Authorize]
    [HttpPost("hold")]
    public async Task<ActionResult<BookingHoldingResponse>> CreateHolding(
        CreateBookingHoldRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.CreateHolding(request, cancellationToken));
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<ActionResult<PaginatedResponse<BookingHoldingResponse>>> GetMyBookings(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.GetMyBookings(page, pageSize, cancellationToken));
    }

    [Authorize]
    [HttpGet("{bookingId:int}")]
    public async Task<ActionResult<BookingHoldingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.GetBooking(bookingId, cancellationToken));
    }

    [Authorize]
    [HttpGet("payment-groups/{paymentGroupId:guid}")]
    public async Task<ActionResult<BookingHoldingGroupResponse>> GetHoldingGroup(Guid paymentGroupId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.GetHoldingGroup(paymentGroupId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{bookingId:int}/pay")]
    public async Task<ActionResult<BookingHoldingResponse>> CompletePayment(
        int bookingId,
        CompleteBookingPaymentRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.CompletePayment(bookingId, request, cancellationToken));
    }

    [Authorize]
    [HttpDelete("{bookingId:int}/hold")]
    public async Task<IActionResult> CancelHolding(int bookingId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.CancelHolding(bookingId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{bookingId:int}/cancel")]
    public async Task<IActionResult> CancelBooking(
        int bookingId,
        CancelPlayerBookingRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.CancelBooking(bookingId, request, cancellationToken));
    }

    [Authorize]
    [HttpPost("{bookingId:int}/retry-payment")]
    public async Task<ActionResult<BookingHoldingResponse>> RetryPayment(int bookingId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _playerBookingService.RetryPayment(bookingId, cancellationToken));
    }

    private void SetCurrentUser() =>
        _playerBookingService.SetCurrentUserId(CurrentUserId());

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private ActionResult<T> ToActionResult<T>(ServiceResult<T> result) =>
        result.Status switch
        {
            ServiceResultStatus.Success => Ok(result.Value),
            ServiceResultStatus.NoContent => NoContent(),
            ServiceResultStatus.BadRequest => BadRequest(result.Error),
            ServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            ServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            ServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            ServiceResultStatus.Conflict => Conflict(result.Error),
            ServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };

    private IActionResult ToActionResult(ServiceResult result) =>
        result.Status switch
        {
            ServiceResultStatus.Success => result.Value is null ? Ok() : Ok(result.Value),
            ServiceResultStatus.NoContent => NoContent(),
            ServiceResultStatus.BadRequest => BadRequest(result.Error),
            ServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            ServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            ServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            ServiceResultStatus.Conflict => Conflict(result.Error),
            ServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };
}
