using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/clubs")]
public class AdminClubsController : ControllerBase
{
    private readonly IAdminClubService _clubService;

    public AdminClubsController(IAdminClubService clubService)
    {
        _clubService = clubService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminClubResponse>>> GetClubs(
        string? search,
        bool? suspendedOnly,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _clubService.ListAsync(
            search,
            suspendedOnly,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpPost("{groupId:int}/moderate")]
    public async Task<ActionResult<AdminClubResponse>> ModerateClub(
        int groupId,
        AdminClubModerationRequest request,
        CancellationToken cancellationToken)
    {
        var moderatorId = CurrentUserId();
        if (moderatorId is null) return Unauthorized();

        var result = await _clubService.ModerateAsync(groupId, request, moderatorId.Value, CurrentUsername(), cancellationToken);
        return result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private string? CurrentUsername() => User.FindFirstValue(ClaimTypes.Name);
}
