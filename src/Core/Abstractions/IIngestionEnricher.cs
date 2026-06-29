using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Optionally enriches an ingestion item using a language model: condenses verbose bodies into concise
/// knowledge notes and proposes semantic relations between items. To keep ingestion safe and the graph
/// consistent, implementations must only reference ids contained in the supplied set of known ids and
/// must never invent new nodes. The default implementation is a no-op, keeping the pipeline fully
/// deterministic and local-only unless an LLM-backed enricher is explicitly configured (see ADR-0001).
/// </summary>
public interface IIngestionEnricher
{
    /// <summary>
    /// Returns a possibly enriched copy of <paramref name="item"/>.
    /// </summary>
    /// <param name="item">The item to enrich.</param>
    /// <param name="knownIds">
    /// Ids of all items known in the current ingestion run. Proposed relations may only target ids in
    /// this set; implementations must not reference or create ids outside it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The enriched item, or the original item unchanged when no enrichment applies.</returns>
    Task<IngestionItem> EnrichAsync(
        IngestionItem item,
        IReadOnlyCollection<string> knownIds,
        CancellationToken cancellationToken = default);
}
