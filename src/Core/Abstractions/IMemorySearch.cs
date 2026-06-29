using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Retrieves short-term memory entries using hybrid search (BM25 + vector similarity + MMR).
/// Replaces plain vector search in the STM pipeline phase for improved recall precision.
/// </summary>
public interface IMemorySearch
{
    /// <summary>
    /// Searches for memory entries relevant to the given query using hybrid scoring.
    /// Results are deduplicated and diversified via Maximal Marginal Relevance (MMR),
    /// then ordered by combined relevance score descending.
    /// </summary>
    /// <param name="query">Natural-language query text.</param>
    /// <param name="userId">User scope — only this user's memories are searched.</param>
    /// <param name="maxResults">Maximum number of results to return. Default: 10.</param>
    /// <param name="lambda">
    /// MMR trade-off parameter. 0.0 = maximise diversity, 1.0 = maximise relevance.
    /// Default: 0.7.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked list of memory entries with individual and combined relevance scores.</returns>
    Task<IReadOnlyList<ScoredMemoryEntry>> SearchAsync(
        string query,
        string userId,
        int maxResults = 10,
        double lambda = 0.7,
        CancellationToken ct = default);
}
