using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// An in-process queue for supervised fire-and-forget background work. Replaces unobserved
/// <c>Task.Run</c> calls: enqueued work is drained by a single hosted consumer and receives a
/// cancellation token that is signalled on host shutdown, so background jobs respect shutdown and
/// surface their failures instead of running detached and swallowing exceptions.
/// </summary>
public interface IBackgroundWorkQueue
{
    /// <summary>
    /// Enqueues a unit of background work. Non-blocking and safe to call from synchronous contexts
    /// (e.g. an endpoint handler or a scope-dispose path).
    /// </summary>
    /// <param name="work">
    /// The work to run. Receives a token that is cancelled when the application begins shutting down.
    /// </param>
    /// <param name="description">A short human-readable label used in failure logs.</param>
    void Enqueue(Func<CancellationToken, Task> work, string description);

    /// <summary>
    /// Awaits and removes the next unit of work, completing when an item becomes available or the
    /// token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the wait (host shutdown).</param>
    /// <returns>The next queued work item.</returns>
    ValueTask<BackgroundWorkItem> DequeueAsync(CancellationToken cancellationToken);
}
