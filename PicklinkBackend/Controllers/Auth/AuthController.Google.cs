using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PicklinkBackend.Startup;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("google")]
    public async Task<ActionResult<AuthResponse>> GoogleLogin(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleLoginAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("google/register")]
    public async Task<ActionResult<AuthResponse>> GoogleRegister(
        GoogleLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.GoogleRegisterAsync(request, cancellationToken);
        return ToActionResult(result);
    }
}