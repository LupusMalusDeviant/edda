using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Enrichment;

/// <summary>
/// No-op <see cref="IIngestionEnricher"/>: returns every item unchanged. This is the default enricher,
/// keeping the ingestion pipeline fully deterministic and local-only. An LLM-backed enricher is only
/// used when explicitly configured (see ADR-0001), so no item content ever leaves the system here.
/// </summary>
public sealed class NullIngestionEnricher : IIngestionEnricher
{
    /// <inheritdoc />
    public Task<IngestionItem> EnrichAsync(
        IngestionItem item,
        IReadOnlyCollection<string> knownIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult(item);
}
