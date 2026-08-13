using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/posts")]
public class AdminPostsController : ControllerBase
{
    private readonly IAdminPostService _postService;

    public AdminPostsController(IAdminPostService postService)
    {
        _postService = postService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminPostResponse>>> GetPosts(
        string? search,
        bool? hiddenOnly,
        int? groupId,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _postService.ListAsync(
            search,
            hiddenOnly,
            groupId,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpPost("{postId:int}/moderate")]
    public async Task<ActionResult<AdminPostResponse>> ModeratePost(
        int postId,
        AdminPostModerationRequest request,
        CancellationToken cancellationToken)
    {
        var moderatorId = CurrentUserId();
        if (moderatorId is null) return Unauthorized();

        var result = await _postService.ModerateAsync(postId, request, moderatorId.Value, CurrentUsername(), cancellationToken);
        return result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpDelete("{postId:int}")]
    public async Task<IActionResult> DeletePost(int postId, CancellationToken cancellationToken)
    {
        var result = await _postService.DeleteAsync(postId, cancellationToken);
        return result.Status switch
        {
            AdminResultStatus.Success => NoContent(),
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
