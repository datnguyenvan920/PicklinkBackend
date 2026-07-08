using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/settings")]
public class AdminSettingsController : ControllerBase
{
    private readonly AdminSettingService _settings;

    public AdminSettingsController(AdminSettingService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminSettingResponse>>> GetSettings(CancellationToken cancellationToken)
    {
        return Ok(await _settings.ListAsync(cancellationToken));
    }

    [HttpPut("{settingKey}")]
    public async Task<ActionResult<AdminSettingResponse>> UpdateSetting(
        string settingKey,
        AdminSettingUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _settings.UpdateAsync(
            settingKey,
            request,
            CurrentUserId(),
            cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<AdminSettingResponse> ToActionResult(AdminSettingUpdateResult result) =>
        result.Status switch
        {
            AdminSettingUpdateResultStatus.Success => Ok(result.Setting),
            AdminSettingUpdateResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AdminSettingUpdateResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}
