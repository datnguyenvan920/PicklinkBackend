using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly AdminUserQueryService _queries;
    private readonly AdminUserLockService _locks;

    public AdminUsersController(
        AdminUserQueryService queries,
        AdminUserLockService locks)
    {
        _queries = queries;
        _locks = locks;
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
        var result = await _queries.ListAsync(
            search,
            role,
            lockedOnly,
            page,
            pageSize,
            cancellationToken);
        return result.IsInvalidRole
            ? BadRequest(new { message = result.ErrorMessage })
            : Ok(result.Users);
    }

    [HttpPost("{userId:int}/lock")]
    public async Task<ActionResult<AdminUserSummaryResponse>> LockUser(
        int userId,
        AdminUserLockRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _locks.LockAsync(userId, CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{userId:int}/unlock")]
    public async Task<ActionResult<AdminUserSummaryResponse>> UnlockUser(
        int userId,
        CancellationToken cancellationToken)
    {
        var result = await _locks.UnlockAsync(userId, cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<AdminUserSummaryResponse> ToActionResult(AdminUserLockResult result) =>
        result.Status switch
        {
            AdminUserLockResultStatus.Success => Ok(result.User),
            AdminUserLockResultStatus.Unauthorized => Unauthorized(),
            AdminUserLockResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminUserLockResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}
