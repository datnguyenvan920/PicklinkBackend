using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Bookings.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/player-reviews")]
public class PlayerReviewController : ControllerBase
{
    private readonly PlayerBookingReviewService _reviews;

    public PlayerReviewController(PlayerBookingReviewService reviews)
    {
        _reviews = reviews;
    }

    [HttpGet("booking/{bookingId:int}")]
    public async Task<ActionResult<BookingReviewResponse>> GetBookingReview(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var result = await _reviews.GetAsync(bookingId, CurrentUserId(), cancellationToken);
        return ToActionResult(result, bookingId);
    }

    [HttpPost("booking/{bookingId:int}")]
    public async Task<ActionResult<BookingReviewResponse>> CreateBookingReview(
        int bookingId,
        CreateBookingReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _reviews.CreateAsync(bookingId, request, CurrentUserId(), cancellationToken);
        return ToActionResult(result, bookingId);
    }

    [HttpGet("venue/{venueId:int}")]
    public async Task<ActionResult<BookingReviewResponse>> GetVenueReview(
        int venueId,
        CancellationToken cancellationToken)
    {
        var result = await _reviews.GetVenueAsync(venueId, CurrentUserId(), cancellationToken);
        return ToActionResult(result, result.Value?.BookingId ?? 0);
    }

    [HttpPut("venue/{venueId:int}")]
    public async Task<ActionResult<BookingReviewResponse>> UpdateVenueReview(
        int venueId,
        CreateBookingReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _reviews.UpdateVenueAsync(venueId, request, CurrentUserId(), cancellationToken);
        return ToActionResult(result, result.Value?.BookingId ?? 0);
    }

    private ActionResult<BookingReviewResponse> ToActionResult(
        PlayerBookingReviewResult result,
        int bookingId) =>
        result.Status switch
        {
            PlayerBookingReviewResultStatus.Success => Ok(result.Value),
            PlayerBookingReviewResultStatus.Created => CreatedAtAction(nameof(GetBookingReview), new { bookingId }, result.Value),
            PlayerBookingReviewResultStatus.Unauthorized => Unauthorized(),
            PlayerBookingReviewResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            PlayerBookingReviewResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}