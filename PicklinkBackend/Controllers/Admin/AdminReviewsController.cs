using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reviews")]
public class AdminReviewsController : ControllerBase
{
    private readonly AdminReviewQueryService _queries;
    private readonly AdminReviewModerationService _moderation;

    public AdminReviewsController(
        AdminReviewQueryService queries,
        AdminReviewModerationService moderation)
    {
        _queries = queries;
        _moderation = moderation;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminReviewResponse>>> GetReviews(
        string? search,
        string? moderationStatus,
        string? targetType,
        int? score,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _queries.ListAsync(
            search,
            moderationStatus,
            targetType,
            score,
            page,
            pageSize,
            cancellationToken));
    }

    [HttpPost("{ratingId:int}/moderate")]
    public async Task<ActionResult<AdminReviewResponse>> ModerateReview(
        int ratingId,
        AdminReviewModerationRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = _moderation.Validate(request);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var reviewerId = CurrentUserId();
        if (reviewerId is null) return Unauthorized();

        var result = await _moderation.ModerateAsync(
            ratingId,
            request,
            reviewerId.Value,
            cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<AdminReviewResponse> ToActionResult(AdminReviewModerationResult result) =>
        result.Status switch
        {
            AdminReviewModerationResultStatus.Success => Ok(result.Review),
            AdminReviewModerationResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminReviewModerationResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}
