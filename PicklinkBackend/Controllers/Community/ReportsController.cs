using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly CommunityReportSubmissionService _reports;

    public ReportsController(CommunityReportSubmissionService reports)
    {
        _reports = reports;
    }

    [HttpPost]
    public async Task<ActionResult<ReportSubmissionResponse>> CreateReport(
        ReportSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _reports.CreateAsync(request, CurrentUserId(), cancellationToken);
        return result.Status switch
        {
            ReportSubmissionResultStatus.Success => Created($"/api/reports/{result.Report!.CommunityReportId}", result.Report),
            ReportSubmissionResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            ReportSubmissionResultStatus.Unauthorized => Unauthorized(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}