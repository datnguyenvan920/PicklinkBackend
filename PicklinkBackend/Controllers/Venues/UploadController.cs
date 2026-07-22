using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PicklinkBackend.Startup;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/upload")]
public class UploadController : ControllerBase
{
    private readonly CloudinarySignatureService _signatures;
    private readonly LocalUploadService _localUploads;

    public UploadController(CloudinarySignatureService signatures, LocalUploadService localUploads)
    {
        _signatures = signatures;
        _localUploads = localUploads;
    }

    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [HttpPost("signature")]
    public IActionResult GenerateSignature([FromBody] SignatureRequest request)
    {
        var result = _signatures.Generate(request);
        return result.Status switch
        {
            CloudinarySignatureResultStatus.Success => Ok(result.Signature),
            CloudinarySignatureResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            CloudinarySignatureResultStatus.NotConfigured => StatusCode(500, new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [HttpPost("club-cover")]
    public async Task<IActionResult> UploadClubCover(IFormFile image, CancellationToken cancellationToken)
    {
        var result = await _localUploads.SaveClubCoverAsync(image, cancellationToken);
        return result.Status switch
        {
            LocalUploadResultStatus.Success => Ok(result.Value),
            LocalUploadResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [EnableRateLimiting(RateLimitPolicies.Upload)]
    [HttpPost("delete")]
    public async Task<IActionResult> DeleteUploadedMedia(
        [FromBody] DeleteUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Url))
        {
            return BadRequest(new { message = "Vui lòng cung cấp URL ảnh cần xóa." });
        }

        var deleted = await _localUploads.DeleteMediaAsync(request.Url, cancellationToken);
        return Ok(new { success = deleted });
    }
}
