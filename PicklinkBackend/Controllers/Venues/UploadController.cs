using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UploadController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public UploadController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("signature")]
    public IActionResult GenerateSignature([FromBody] SignatureRequest request)
    {
        var cloudName = _configuration["Cloudinary:CloudName"];
        var apiKey = _configuration["Cloudinary:ApiKey"];
        var apiSecret = _configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            return StatusCode(500, new { message = "Cloudinary is not configured on the server." });
        }

        if (!CloudinarySignaturePolicy.TryValidate(request.Parameters, out var parametersToSign))
        {
            return BadRequest(new { message = "Unsupported Cloudinary signature parameters." });
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        parametersToSign["timestamp"] = timestamp;
        var filteredParams = parametersToSign
            .OrderBy(p => p.Key)
            .Select(p => $"{p.Key}={p.Value}");

        var stringToSign = string.Join("&", filteredParams) + apiSecret;

        // Compute SHA-1 hash for Cloudinary signature
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(stringToSign));
        var signature = Convert.ToHexString(hash).ToLowerInvariant();

        return Ok(new SignatureResponse
        {
            Signature = signature,
            Timestamp = timestamp,
            ApiKey = apiKey,
            CloudName = cloudName
        });
    }
}

public class SignatureRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
}

public class SignatureResponse
{
    public string Signature { get; set; } = null!;
    public string Timestamp { get; set; } = null!;
    public string ApiKey { get; set; } = null!;
    public string CloudName { get; set; } = null!;
}
