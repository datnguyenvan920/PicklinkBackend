using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleLoginAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("google/register")]
    public async Task<ActionResult<AuthResponse>> GoogleRegister(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleRegisterAsync(request, cancellationToken);
        return ToActionResult(result);
    }
}