using Edda.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Embeddings;

/// <summary>
/// Background service that keeps the embedding cache complete. It periodically asks the
/// <see cref="INeo4jEmbeddingCache"/> to embed any rules that still lack chunks. This is resilient and
/// resumable: the graph itself is the durable work-list, so progress survives restarts, and a single rule
/// that repeatedly fails to embed is skipped after a retry cap rather than blocking the rest of the corpus.
/// The loop idles cheaply once coverage is complete and picks up newly ingested rules on the next cycle.
/// <para>
/// This replaces the previous one-shot startup rebuild, which aborted the whole run after a handful of
/// provider failures and never resumed — leaving most of a large corpus unembedded.
/// </para>
/// </summary>
internal sealed class EmbeddingBackfillHostedService : IHostedService
{
    private readonly INeo4jEmbeddingCache _cache;
    private readonly IHeadVectorStore _headVectors;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<EmbeddingBackfillHostedService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _initialDelay;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of <see cref="EmbeddingBackfillHostedService"/>.
    /// </summary>
    /// <param name="cache">The embedding cache to drain.</param>
    /// <param name="headVectors">Head-vector store whose centroids are recomputed after each chunk-embedding pass.</param>
    /// <param name="embeddings">Embedding service; the loop is a no-op while it is unavailable.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="intervalSeconds">Seconds between backfill cycles once running (clamped to at least 5).</param>
    /// <param name="initialDelaySeconds">
    /// Seconds to wait after startup before the first cycle, letting seeding settle (clamped to at least 0).
    /// </param>
    public EmbeddingBackfillHostedService(
        INeo4jEmbeddingCache cache,
        IHeadVectorStore headVectors,
        IEmbeddingService embeddings,
        ILogger<EmbeddingBackfillHostedService> logger,
        int intervalSeconds = 60,
        int initialDelaySeconds = 10)
    {
        _cache = cache;
        _headVectors = headVectors;
        _embeddings = embeddings;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(Math.Max(5, intervalSeconds));
        _initialDelay = TimeSpan.FromSeconds(Math.Max(0, initialDelaySeconds));
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        // Abort any in-flight rebuild so shutdown stays prompt.
        _cache.CancelRebuild();

        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown grace period elapsed — let the background loop unwind on its own.
            }
        }
    }

    /// <summary>
    /// The backfill loop: after an initial settle delay, repeatedly drains pending embeddings, then waits
    /// one interval before re-checking. Per-cycle failures are logged and retried on the next cycle.
    /// </summary>
    /// <param name="ct">Token cancelled on shutdown.</param>
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_initialDelay, ct).ConfigureAwait(false);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_embeddings.IsAvailable)
                    {
                        // First fill chunk embeddings. Head centroids only need recomputing when this cycle
                        // actually wrote chunks (re-embedding flags the affected heads dirty); an idle cycle
                        // that embedded nothing leaves every head clean, so skip the head-vector pass and
                        // avoid re-querying/re-clustering unchanged repositories on every backfill tick.
                        var embedded = await _cache.RebuildAsync(ct).ConfigureAwait(false);
                        if (embedded > 0)
                            await _headVectors.RebuildAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Embedding backfill cycle failed; retrying next cycle | {Component}", "AKG");
                }

                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
