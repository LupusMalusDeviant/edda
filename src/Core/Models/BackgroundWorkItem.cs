namespace Edda.Core.Models;

/// <summary>
/// A single unit of supervised background work: an asynchronous delegate together with a short
/// description used in failure diagnostics. Enqueued via <c>IBackgroundWorkQueue</c> and executed by
/// the queue's hosted consumer.
/// </summary>
/// <param name="Work">
/// The asynchronous work to execute. Receives a cancellation token that is signalled when the
/// application begins shutting down, so long-running jobs can stop promptly.
/// </param>
/// <param name="Description">A short human-readable label used in failure logs.</param>
public sealed record BackgroundWorkItem(Func<CancellationToken, Task> Work, string Description);
