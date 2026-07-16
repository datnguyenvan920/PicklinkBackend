using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services.Payments;

namespace PicklinkBackend.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/payments/webhooks/sepay")]
public sealed class SePayWebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;

    public SePayWebhookController(IConfiguration configuration, IServiceProvider services)
    {
        _configuration = configuration;
        _services = services;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var secret = _configuration["SePay:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return StatusCode(503, new { success = false, message = "SePay webhook is not configured." });

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        if (!SePayWebhookSecurity.Verify(rawBody,
                Request.Headers["X-SePay-Timestamp"].FirstOrDefault(),
                Request.Headers["X-SePay-Signature"].FirstOrDefault(),
                secret,
                DateTimeOffset.UtcNow))
            return Unauthorized(new { success = false, message = "Invalid SePay signature." });

        SePayWebhookRequest? request;
        try { request = JsonSerializer.Deserialize<SePayWebhookRequest>(rawBody); }
        catch (JsonException) { return BadRequest(new { success = false, message = "Invalid SePay payload." }); }

        if (request is null) return BadRequest(new { success = false, message = "Invalid SePay payload." });
        var service = ActivatorUtilities.CreateInstance<SePayWebhookService>(_services);
        var result = await service.Process(request, cancellationToken);
        return StatusCode(result.StatusCode, new { success = result.Success, message = result.Message });
    }
}
