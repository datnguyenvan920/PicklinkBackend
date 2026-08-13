using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Admin.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/reports")]
public class AdminReportsController : ControllerBase
{
    private readonly IAdminReportService _reportService;

    public AdminReportsController(IAdminReportService reportService)
    {
        _reportService = reportService;
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
        return Ok(await _reportService.ListAsync(search, status, targetType, page, pageSize, cancellationToken));
    }

    [HttpPost("{reportId:int}/review")]
    public async Task<ActionResult<AdminReportResponse>> ReviewReport(
        int reportId,
        AdminReportReviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _reportService.ReviewAsync(reportId, request, CurrentUserId(), CurrentUsername(), cancellationToken);
        return result.Status switch
        {
            AdminResultStatus.Success => Ok(result.Value),
            AdminResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminResultStatus.Unauthorized => Unauthorized(),
            AdminResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            AdminResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private string? CurrentUsername() => User.FindFirstValue(ClaimTypes.Name);
}
