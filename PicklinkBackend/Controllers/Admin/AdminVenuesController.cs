using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Admin.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/venues")]
public class AdminVenuesController : ControllerBase
{
    private readonly IAdminVenueService _venueService;

    public AdminVenuesController(IAdminVenueService venueService)
    {
        _venueService = venueService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminVenueSummaryResponse>>> GetVenues(
        string? search,
        string? status,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _venueService.ListAsync(search, status, page, pageSize, cancellationToken);
        return !result.IsSuccess
            ? BadRequest(new { message = result.ErrorMessage })
            : Ok(result.Value);
    }

    [HttpGet("{venueId:int}")]
    public async Task<ActionResult<AdminVenueDetailResponse>> GetVenue(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await _venueService.GetDetailAsync(venueId, cancellationToken);
        return venue is null
            ? NotFound(new { message = "Không tìm thấy cụm sân." })
            : Ok(venue);
    }

    [HttpPost("{venueId:int}/approve")]
    public async Task<ActionResult<AdminVenueDetailResponse>> ApproveVenue(
        int venueId,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null) return Unauthorized();

        var result = await _venueService.ApproveAsync(venueId, actorId.Value, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{venueId:int}/reject")]
    public async Task<ActionResult<AdminVenueDetailResponse>> RejectVenue(
        int venueId,
        AdminVenueRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null) return Unauthorized();

        var result = await _venueService.RejectAsync(
            venueId,
            request.Reason,
            actorId.Value,
            cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<AdminVenueDetailResponse> ToActionResult(AdminVenueApprovalResult result) =>
        result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.Unauthorized => Unauthorized(),
            AdminResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            AdminResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}
