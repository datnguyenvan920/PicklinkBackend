using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/realtime/notifications")]
public sealed class NotificationRealtimeController : ControllerBase
{
    private readonly NotificationRealtimeNotifier _notifier;

    public NotificationRealtimeController(NotificationRealtimeNotifier notifier) =>
        _notifier = notifier;

    [HttpGet]
    public async Task<ActionResult> Stream(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.Append("X-Accel-Buffering", "no");
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await Response.WriteAsync("retry: 3000\n: connected\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
        using var subscription = _notifier.Subscribe(userId.Value);
        var waitForEvent = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(
                    waitForEvent,
                    Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
                if (completed == waitForEvent)
                {
                    if (!await waitForEvent) break;
                    while (subscription.Reader.TryRead(out var notification))
                    {
                        var json = JsonSerializer.Serialize(
                            notification,
                            new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        await Response.WriteAsync(
                            $"event: notification-updated\ndata: {json}\n\n",
                            cancellationToken);
                    }
                    waitForEvent = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                }
                else
                {
                    await Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                }
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }

        return new EmptyResult();
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
}
