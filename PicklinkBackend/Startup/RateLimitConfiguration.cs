using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PicklinkBackend.Startup;

internal static class RateLimitConfiguration
{
    internal static IServiceCollection AddPicklinkRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = RejectAsync;
            options.AddPolicy(RateLimitPolicies.Authentication, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => NewFixedWindowOptions(5, TimeSpan.FromMinutes(1))));
            options.AddPolicy(RateLimitPolicies.Messaging, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ActorKey(context),
                    _ => NewFixedWindowOptions(30, TimeSpan.FromMinutes(1))));
            options.AddPolicy(RateLimitPolicies.Upload, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ActorKey(context),
                    _ => NewFixedWindowOptions(10, TimeSpan.FromMinutes(1))));
        });

        return services;
    }

    private static string ActorKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    private static FixedWindowRateLimiterOptions NewFixedWindowOptions(
        int permitLimit,
        TimeSpan window) => new()
    {
        PermitLimit = permitLimit,
        Window = window,
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true
    };

    private static ValueTask RejectAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var task = context.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = "Bạn thao tác quá nhanh. Vui lòng chờ một lúc rồi thử lại."
        }, cancellationToken);
        return new ValueTask(task);
    }
}
