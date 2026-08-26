using System;
using System.Threading;
using System.Threading.Tasks;

namespace PicklinkBackend.Services.Matches;

/// <summary>
/// Lets request-handling code wake <see cref="MatchmakingWorker"/> immediately after a queue
/// change instead of waiting for its Firebase push or periodic-timer fallback.
/// </summary>
public class MatchmakingScanTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestScan()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A scan is already pending; the worker will pick up this change when it runs.
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);
}
