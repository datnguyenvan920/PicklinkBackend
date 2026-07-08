using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class AuthController
{
    [HttpPost("forgot-password")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ForgotPasswordAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("verify-reset-code")]
    public async Task<ActionResult<object>> VerifyResetCode(
        VerifyPasswordResetCodeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.VerifyResetCodeAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult<object>> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request, cancellationToken);
        return ToActionResult(result);
    }
}