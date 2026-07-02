using System.Threading.Channels;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Background;

/// <summary>
/// <see cref="IBackgroundWorkQueue"/> backed by an unbounded <see cref="Channel{T}"/> with a single
/// reader (the hosted consumer) and multiple writers (the enqueueing call sites).
/// <para>
/// The channel is unbounded so <see cref="Enqueue"/> never blocks and never drops work: the call
/// sites (an admin endpoint returning 202, a bulk-ingestion scope closing, startup wiring) run in
/// synchronous or latency-sensitive contexts, and the queued items are few and coarse-grained
/// (embedding rebuilds, superseded-rule invalidation), so unbounded growth is not a practical risk.
/// </para>
/// </summary>
internal sealed class ChannelBackgroundWorkQueue : IBackgroundWorkQueue
{
    private readonly Channel<BackgroundWorkItem> _channel =
        Channel.CreateUnbounded<BackgroundWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <inheritdoc/>
    public void Enqueue(Func<CancellationToken, Task> work, string description)
    {
        ArgumentNullException.ThrowIfNull(work);

        // An unbounded channel accepts every write synchronously, so this always succeeds without
        // blocking the caller.
        _channel.Writer.TryWrite(new BackgroundWorkItem(work, description ?? string.Empty));
    }

    /// <inheritdoc/>
    public ValueTask<BackgroundWorkItem> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}
