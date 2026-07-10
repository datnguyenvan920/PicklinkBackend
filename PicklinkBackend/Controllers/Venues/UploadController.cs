using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("club-cover")]
    public async Task<IActionResult> UploadClubCover([FromForm] IFormFile image, CancellationToken cancellationToken)
    {
        var result = await _localUploads.SaveClubCoverAsync(image, cancellationToken);
        return result.Status switch
        {
            LocalUploadResultStatus.Success => Ok(result.Value),
            LocalUploadResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
}
