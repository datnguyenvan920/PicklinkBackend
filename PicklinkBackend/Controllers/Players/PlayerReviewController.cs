using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

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

    private ActionResult<BookingReviewResponse> ToActionResult(
        PlayerBookingReviewResult result,
        int bookingId) =>
        result.Status switch
        {
            PlayerBookingReviewResultStatus.Success => Ok(result.Review),
            PlayerBookingReviewResultStatus.Created => CreatedAtAction(nameof(GetBookingReview), new { bookingId }, result.Review),
            PlayerBookingReviewResultStatus.Unauthorized => Unauthorized(),
            PlayerBookingReviewResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            PlayerBookingReviewResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}