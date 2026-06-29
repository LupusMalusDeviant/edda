namespace Edda.AKG.Embeddings;

/// <summary>
/// Manages embedding caching in Neo4j: only re-embeds rules whose body text changed
/// since the last cache build, and exposes rebuild progress for status reporting.
/// </summary>
internal interface INeo4jEmbeddingCache
{
    /// <summary>Total number of rules scheduled to be embedded in the current rebuild.</summary>
    int TotalToEmbed { get; }

    /// <summary>Number of rules embedded so far in the current rebuild.</summary>
    int EmbeddedSoFar { get; }

    /// <summary>Whether a rebuild is currently in progress.</summary>
    bool IsRebuilding { get; }

    /// <summary>The rule id currently being embedded, or <see langword="null"/> when idle.</summary>
    string? CurrentRuleId { get; }

    /// <summary>Rebuilds embeddings for all rules whose body text changed since the last build.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the rebuild finishes.</returns>
    Task RebuildAsync(CancellationToken ct);

    /// <summary>Requests cancellation of an in-progress rebuild. No-op if none is running.</summary>
    void CancelRebuild();

    /// <summary>
    /// Returns embedding coverage across all rules: <c>Embedded</c> (≥1 chunk), <c>Pending</c> (no chunk yet,
    /// still under the retry cap), <c>Failed</c> (no chunk, hit the retry cap), and <c>Total</c> rule count.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of (Embedded, Pending, Failed, Total) rule counts.</returns>
    Task<(int Embedded, int Pending, int Failed, int Total)> GetCoverageAsync(CancellationToken ct);

    /// <summary>Ensures the Neo4j vector index required for semantic search exists.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the index is present.</returns>
    Task EnsureVectorIndexAsync(CancellationToken ct);

    /// <summary>Embeds a single rule body and stores the vector and its hash on the rule node.</summary>
    /// <param name="ruleId">The id of the rule to embed.</param>
    /// <param name="body">The rule body text to embed.</param>
    /// <param name="chunkStyle">Optional forced chunking style (null = auto-detect).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the embedding is stored.</returns>
    Task EmbedSingleAsync(string ruleId, string body, string? chunkStyle, CancellationToken ct);

    /// <summary>Determines whether a rule already has an up-to-date embedding for the given body hash.</summary>
    /// <param name="ruleId">The id of the rule to check.</param>
    /// <param name="bodyHash">The SHA-256 hash of the current rule body.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if a current embedding exists; otherwise <see langword="false"/>.</returns>
    Task<bool> HasEmbeddingAsync(string ruleId, string bodyHash, CancellationToken ct);
}
