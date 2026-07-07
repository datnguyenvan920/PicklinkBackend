using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    [Authorize]
    [HttpGet("role-status")]
    public async Task<ActionResult<object>> RoleStatus(CancellationToken cancellationToken)
    {
        var result = await _authService.GetRoleStatusAsync(CurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [Authorize]
    [HttpPost("assign-role")]
    public async Task<ActionResult<object>> AssignRole(AssignRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.AssignRoleAsync(CurrentUserId(), request, cancellationToken);
        return ToActionResult(result);
    }
}