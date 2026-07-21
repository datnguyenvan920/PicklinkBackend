using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PicklinkBackend.Startup;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ForgotPasswordAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("verify-reset-code")]
    public async Task<ActionResult<object>> VerifyResetCode(
        VerifyPasswordResetCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.VerifyResetCodeAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [HttpPost("reset-password")]
    public async Task<ActionResult<object>> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request, cancellationToken);
        return ToActionResult(result);
    }
}