using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Owner;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner/staff")]
public class OwnerStaffController : ControllerBase
{
    private readonly OwnerStaffService _staff;

    public OwnerStaffController(OwnerStaffService staff)
    {
        _staff = staff;
    }

    [HttpGet]
    public async Task<ActionResult<List<OwnerStaffResponse>>> GetStaff(CancellationToken cancellationToken)
    {
        var result = await _staff.ListAsync(CurrentUserId(), cancellationToken);
        return result.Status == OwnerStaffResultStatus.Unauthorized
            ? Unauthorized()
            : Ok(result.Staff);
    }

    [HttpPost]
    public async Task<ActionResult<OwnerStaffResponse>> AssignStaff(
        AssignStaffRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _staff.AssignAsync(request, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("accounts")]
    public async Task<ActionResult<OwnerStaffResponse>> CreateStaffAccount(
        CreateStaffAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _staff.CreateAccountAsync(request, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPatch("{staffId:int}")]
    public async Task<ActionResult<OwnerStaffResponse>> UpdateStaff(
        int staffId,
        UpdateStaffRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _staff.UpdateAsync(staffId, request, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("check-in-history")]
    public async Task<ActionResult<PaginatedResponse<OwnerCheckInHistoryResponse>>> GetCheckInHistory(
        int? venueId,
        DateOnly? date,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _staff.GetCheckInHistoryAsync(
            venueId,
            date,
            page,
            pageSize,
            CurrentUserId(),
            cancellationToken);
        return result.Status == OwnerStaffResultStatus.Unauthorized
            ? Unauthorized()
            : Ok(result.History);
    }

    private ActionResult<OwnerStaffResponse> ToActionResult(OwnerStaffMutationResult result) =>
        result.Status switch
        {
            OwnerStaffResultStatus.Success => Ok(result.Staff),
            OwnerStaffResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            OwnerStaffResultStatus.Unauthorized => Unauthorized(),
            OwnerStaffResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            OwnerStaffResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}