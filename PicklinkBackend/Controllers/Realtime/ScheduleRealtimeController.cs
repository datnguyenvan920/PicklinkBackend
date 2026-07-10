using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services.Schedules;

namespace PicklinkBackend.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/realtime/schedule")]
public sealed class ScheduleRealtimeController : ControllerBase
{
    private readonly ScheduleRealtimeNotifier _notifier;

    public ScheduleRealtimeController(ScheduleRealtimeNotifier notifier)
    {
        _notifier = notifier;
    }

    [HttpGet]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.Append("X-Accel-Buffering", "no");
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await Response.WriteAsync("retry: 3000\n: connected\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        using var subscription = _notifier.Subscribe();
        var waitForEvent = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                var completed = await Task.WhenAny(waitForEvent, heartbeat);
                if (completed == waitForEvent)
                {
                    if (!await waitForEvent) break;
                    while (subscription.Reader.TryRead(out var notification))
                    {
                        var json = JsonSerializer.Serialize(notification, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                        await Response.WriteAsync($"event: schedule-updated\ndata: {json}\n\n", cancellationToken);
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
    }
}
