using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Fetches WorldKnowledge nodes from Neo4j that are relevant to the given concepts.
/// WorldKnowledge nodes provide general factual context without ranking constraints.
/// </summary>
internal sealed class WorldKnowledgeFetcher
{
    private const int MaxWorldKnowledgeResults = 10;

    private readonly ICypherExecutor _cypher;

    /// <summary>
    /// Initializes a new instance of <see cref="WorldKnowledgeFetcher"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor for graph queries.</param>
    internal WorldKnowledgeFetcher(ICypherExecutor cypher) => _cypher = cypher;

    /// <summary>
    /// Returns WorldKnowledge rules matching any of the provided concepts.
    /// Returns an empty list if no concepts are given or no matches are found.
    /// </summary>
    /// <param name="concepts">Extracted task concepts to match against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Matching world knowledge rules, limited to <c>10</c> results.</returns>
    internal async Task<IReadOnlyList<KnowledgeRule>> FetchAsync(
        IReadOnlyList<string> concepts,
        CancellationToken ct)
    {
        if (concepts.Count == 0) return [];

        var rows = await _cypher.QueryAsync(
            $"""
            MATCH (w:WorldKnowledge)
            WHERE any(c IN $concepts WHERE
                toLower(w.domain) CONTAINS toLower(c)
                OR any(t IN w.tags WHERE toLower(t) CONTAINS toLower(c)))
            RETURN w
            LIMIT {MaxWorldKnowledgeResults}
            """,
            new { concepts },
            ct).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("w", out var w) ? w : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }
}
