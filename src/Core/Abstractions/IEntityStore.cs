using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists and queries the graph entity layer (LightRAG-style <c>(:Entity)</c> nodes and
/// <c>[:RELATES_TO]</c> edges). All operations are user-scoped (Regel 7): entities are keyed by
/// <c>(ownerId, normalizedName)</c> and retrieval is restricted to the requesting user.
/// </summary>
public interface IEntityStore
{
    /// <summary>
    /// Upserts the extracted entities and relations for a user. Entities are deduplicated by
    /// normalized name; repeat ingests increment a mention/weight counter rather than duplicating.
    /// </summary>
    /// <param name="extraction">The entities and relations to persist.</param>
    /// <param name="userId">Owner of the entities.</param>
    /// <param name="sourceType">Provenance label (e.g. "chat", "web", "codebase").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of entities and relations ingested.</returns>
    Task<EntityIngestionResult> IngestAsync(
        EntityExtractionResult extraction,
        string userId,
        string sourceType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds entities whose name contains any of the given terms (case-insensitive), scoped to the user.
    /// </summary>
    /// <param name="terms">Search terms.</param>
    /// <param name="userId">User scope; null returns entities across all owners.</param>
    /// <param name="limit">Maximum number of entities to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching entities.</returns>
    Task<IReadOnlyList<GraphEntity>> FindEntitiesAsync(
        IReadOnlyList<string> terms,
        string? userId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the 1-hop neighborhood of an entity across <c>[:RELATES_TO]</c> edges (both directions).
    /// </summary>
    /// <param name="entityName">Name of the center entity.</param>
    /// <param name="userId">User scope; null searches across all owners.</param>
    /// <param name="limit">Maximum number of neighbors to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Neighboring entities.</returns>
    Task<IReadOnlyList<GraphEntity>> GetRelatedAsync(
        string entityName,
        string? userId,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
