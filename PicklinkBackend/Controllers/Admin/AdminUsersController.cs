using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Admin.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _userService;

    public AdminUsersController(IAdminUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminUserSummaryResponse>>> GetUsers(
        string? search,
        string? role,
        bool lockedOnly = false,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _userService.ListAsync(
            search,
            role,
            lockedOnly,
            page,
            pageSize,
            cancellationToken);
        return !result.IsSuccess
            ? BadRequest(new { message = result.ErrorMessage })
            : Ok(result.Value);
    }

    [HttpPost("owners")]
    public async Task<ActionResult<AdminUserSummaryResponse>> CreateVenueOwner(
        AdminCreateVenueOwnerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _userService.CreateVenueOwnerAsync(request, cancellationToken);
        return ToActionResult(result);
    }
    [HttpPost("{userId:int}/lock")]
    public async Task<ActionResult<AdminUserSummaryResponse>> LockUser(
        int userId,
        AdminUserLockRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _userService.LockAsync(userId, request?.Reason, CurrentUserId(), CurrentUsername(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{userId:int}/unlock")]
    public async Task<ActionResult<AdminUserSummaryResponse>> UnlockUser(
        int userId,
        CancellationToken cancellationToken)
    {
        var result = await _userService.UnlockAsync(userId, CurrentUserId(), CurrentUsername(), cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<AdminUserSummaryResponse> ToActionResult(AdminUserLockResult result) =>
        result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.Unauthorized => Unauthorized(),
            AdminResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            AdminResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private string? CurrentUsername() => User.FindFirstValue(ClaimTypes.Name);
}
