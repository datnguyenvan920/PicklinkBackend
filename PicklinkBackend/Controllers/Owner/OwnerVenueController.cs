using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner")]
public class OwnerVenueController : ControllerBase
{
    private readonly OwnerVenueService _ownerVenueService;

    public OwnerVenueController(OwnerVenueService ownerVenueService)
    {
        _ownerVenueService = ownerVenueService;
    }

    [HttpGet("venues")]
    public async Task<ActionResult<List<OwnerVenueResponse>>> GetVenues(CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.GetVenues(cancellationToken));
    }

    [HttpGet("venues/{venueId:int}")]
    public async Task<ActionResult<OwnerVenueResponse>> GetVenue(int venueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.GetVenue(venueId, cancellationToken));
    }

    [HttpPost("venues")]
    public async Task<ActionResult<OwnerVenueResponse>> CreateVenue(OwnerVenueUpsertRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.CreateVenue(request, cancellationToken));
    }

    [HttpPut("venues/{venueId:int}")]
    public async Task<ActionResult<OwnerVenueResponse>> UpdateVenue(int venueId, OwnerVenueUpsertRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.UpdateVenue(venueId, request, cancellationToken));
    }

    [HttpPatch("venues/{venueId:int}/open-status")]
    public async Task<ActionResult<OwnerVenueResponse>> SetVenueOpenStatus(int venueId, OwnerVenueOpenStatusRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.SetVenueOpenStatus(venueId, request, cancellationToken));
    }

    [HttpPost("venues/{venueId:int}/submit")]
    public async Task<ActionResult<OwnerVenueResponse>> SubmitVenueForApproval(int venueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.SubmitVenueForApproval(venueId, cancellationToken));
    }

    [HttpGet("venues/{venueId:int}/listing-fee/preview")]
    public async Task<ActionResult<OwnerListingFeePreviewResponse>> PreviewListingFee(
        int venueId,
        int months = 1,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.PreviewListingFee(venueId, months, cancellationToken));
    }

    [HttpPost("venues/{venueId:int}/listing-fee/payments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
    public async Task<ActionResult<OwnerListingFeePaymentResponse>> SubmitListingFeePayment(
        int venueId,
        [FromForm] OwnerListingFeePaymentRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.SubmitListingFeePayment(venueId, request, cancellationToken));
    }

    [HttpPost("venues/{venueId:int}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024 + 1024 * 100)]
    public async Task<ActionResult<OwnerVenueImageResponse>> UploadVenueImage(
        int venueId,
        [FromForm] OwnerVenueImageUploadRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.UploadVenueImage(venueId, request, cancellationToken));
    }

    [HttpPatch("venues/{venueId:int}/images/{imageId:int}/primary")]
    public async Task<ActionResult<OwnerVenueResponse>> SetPrimaryVenueImage(int venueId, int imageId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.SetPrimaryVenueImage(venueId, imageId, cancellationToken));
    }

    [HttpDelete("venues/{venueId:int}/images/{imageId:int}")]
    public async Task<IActionResult> DeleteVenueImage(int venueId, int imageId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.DeleteVenueImage(venueId, imageId, cancellationToken));
    }

    [HttpDelete("venues/{venueId:int}")]
    public async Task<IActionResult> DeleteVenue(int venueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.DeleteVenue(venueId, cancellationToken));
    }

    [HttpPost("venues/{venueId:int}/courts")]
    public async Task<ActionResult<OwnerCourtResponse>> CreateCourt(int venueId, OwnerCourtUpsertRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.CreateCourt(venueId, request, cancellationToken));
    }

    [HttpPut("courts/{courtId:int}")]
    public async Task<ActionResult<OwnerCourtResponse>> UpdateCourt(int courtId, OwnerCourtUpsertRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.UpdateCourt(courtId, request, cancellationToken));
    }

    [HttpDelete("courts/{courtId:int}")]
    public async Task<IActionResult> DeleteCourt(int courtId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.DeleteCourt(courtId, cancellationToken));
    }

    [HttpGet("schedule")]
    public async Task<ActionResult<OwnerScheduleResponse>> GetScheduleV2(
        DateOnly date,
        string view = "day",
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.GetScheduleV2(date, view, cancellationToken));
    }

    [HttpGet("schedule/legacy")]
    public async Task<ActionResult<OwnerScheduleResponse>> GetSchedule(DateOnly date, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.GetSchedule(date, cancellationToken));
    }

    [HttpPost("schedule/entries")]
    public async Task<ActionResult<OwnerScheduleItemResponse>> CreateScheduleEntry(OwnerScheduleBlockRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.CreateScheduleEntry(request, cancellationToken));
    }

    [HttpPost("schedule/blocks")]
    public async Task<ActionResult<OwnerScheduleItemResponse>> CreateBlock(OwnerScheduleBlockRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.CreateBlock(request, cancellationToken));
    }

    [HttpDelete("schedule/entries/{bookingId:int}")]
    public async Task<IActionResult> DeleteScheduleEntry(int bookingId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.DeleteScheduleEntry(bookingId, cancellationToken));
    }

    [HttpDelete("schedule/blocks/{bookingId:int}")]
    public async Task<IActionResult> DeleteBlock(int bookingId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.DeleteBlock(bookingId, cancellationToken));
    }

    [HttpPatch("bookings/{bookingId:int}/status")]
    public async Task<IActionResult> UpdateBookingStatus(int bookingId, OwnerBookingStatusRequest request, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _ownerVenueService.UpdateBookingStatus(bookingId, request, cancellationToken));
    }

    private void SetCurrentUser() =>
        _ownerVenueService.SetCurrentUserId(CurrentUserId());

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private ActionResult<T> ToActionResult<T>(ServiceResult<T> result) =>
        result.Status switch
        {
            ServiceResultStatus.Success => Ok(result.Value),
            ServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
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
            ServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
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