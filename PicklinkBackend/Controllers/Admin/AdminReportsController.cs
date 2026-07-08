using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reports")]
public class AdminReportsController : ControllerBase
{
    private readonly AdminReportQueryService _queries;
    private readonly AdminReportReviewService _reviews;

    public AdminReportsController(
        AdminReportQueryService queries,
        AdminReportReviewService reviews)
    {
        _queries = queries;
        _reviews = reviews;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminReportResponse>>> GetReports(
        string? search,
        string? status,
        string? targetType,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _queries.ListAsync(search, status, targetType, page, pageSize, cancellationToken));
    }

    [HttpPost("{reportId:int}/review")]
    public async Task<ActionResult<AdminReportResponse>> ReviewReport(
        int reportId,
        AdminReportReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _reviews.ReviewAsync(reportId, request, CurrentUserId(), cancellationToken);
        return result.Status switch
        {
            AdminReportReviewResultStatus.Success => Ok(result.Report),
            AdminReportReviewResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminReportReviewResultStatus.Unauthorized => Unauthorized(),
            AdminReportReviewResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}