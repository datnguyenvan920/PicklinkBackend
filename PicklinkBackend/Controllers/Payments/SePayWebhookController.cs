using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services.Payments;
using PicklinkBackend.Services.Payments.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/payments/webhooks/sepay")]
public sealed class SePayWebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly ILogger<SePayWebhookController> _logger;

    public SePayWebhookController(
        IConfiguration configuration,
        IServiceProvider services,
        ILogger<SePayWebhookController> logger)
    {
        _configuration = configuration;
        _services = services;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var secret = _configuration["SePay:WebhookSecret"];
        var apiToken = _configuration["SePay:ApiToken"];
        if (string.IsNullOrWhiteSpace(secret) && string.IsNullOrWhiteSpace(apiToken))
        {
            _logger.LogError("[SePay Webhook] Webhook secret / API token is not configured on the server.");
            return StatusCode(503, new { success = false, message = "SePay webhook is not configured." });
        }

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        _logger.LogInformation("==================================================");
        _logger.LogInformation("[SePay Webhook] INCOMING WEBHOOK RECEIVED at {Time}", DateTimeOffset.UtcNow);
        _logger.LogInformation("[SePay Webhook] Raw Payload:\n{RawBody}", rawBody);
        _logger.LogInformation("==================================================");

        if (!SePayWebhookSecurity.VerifyRequest(rawBody,
                Request.Headers,
                Request.Query,
                secret,
                apiToken,
                DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("[SePay Webhook] Authentication failed! Invalid API Key or HMAC signature.");
            return Unauthorized(new { success = false, message = "Invalid SePay signature or API Key." });
        }

        SePayWebhookRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<SePayWebhookRequest>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[SePay Webhook] JSON deserialization failed for payload: {RawBody}", rawBody);
            return BadRequest(new { success = false, message = "Invalid SePay payload." });
        }

        if (request is null)
        {
            _logger.LogWarning("[SePay Webhook] Payload deserialized to null.");
            return BadRequest(new { success = false, message = "Invalid SePay payload." });
        }

        _logger.LogInformation("[SePay Webhook] Parsed: ID={Id}, Account={Account}, Amount={Amount:N0} VND, Content='{Content}', Code='{Code}', Type={Type}",
            request.Id, request.AccountNumber, request.TransferAmount, request.Content, request.Code, request.TransferType);

        var service = ActivatorUtilities.CreateInstance<SePayWebhookService>(_services);
        var result = await service.Process(request, cancellationToken);

        _logger.LogInformation("[SePay Webhook] Result for ID={Id}: StatusCode={StatusCode}, Success={Success}, Message='{Message}'",
            request.Id, result.StatusCode, result.Success, result.Message ?? "OK");

        return StatusCode(result.StatusCode, new { success = result.Success, message = result.Message });
    }
}
