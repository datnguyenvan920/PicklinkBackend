using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private const long MaxAvatarBytes = 2 * 1024 * 1024;
    private readonly PlayerProfileService _profiles;

    public ProfileController(PlayerProfileService profiles)
    {
        _profiles = profiles;
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserProfileResponse>> Me(CancellationToken cancellationToken)
    {
        var result = await _profiles.GetMeAsync(GetCurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    [AllowAnonymous]
    [HttpGet("players/{playerId:int}")]
    public async Task<ActionResult<PublicPlayerProfileResponse>> GetPublicPlayerProfile(
        int playerId,
        CancellationToken cancellationToken)
    {
        var result = await _profiles.GetPublicPlayerProfileAsync(playerId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("me/avatar")]
    [RequestSizeLimit(MaxAvatarBytes + 1024 * 100)]
    public async Task<ActionResult<UserProfileResponse>> UploadAvatar(
        IFormFile avatar,
        CancellationToken cancellationToken)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await _profiles.UploadAvatarAsync(avatar, GetCurrentUserId(), baseUrl, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("me")]
    public async Task<ActionResult<UserProfileResponse>> UpdateMe(
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _profiles.UpdateMeAsync(request, GetCurrentUserId(), cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(PlayerProfileResult<T> result) =>
        result.Status switch
        {
            PlayerProfileResultStatus.Success => Ok(result.Value),
            PlayerProfileResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            PlayerProfileResultStatus.Unauthorized => Unauthorized(),
            PlayerProfileResultStatus.NotFound => NotFound(),
            PlayerProfileResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}