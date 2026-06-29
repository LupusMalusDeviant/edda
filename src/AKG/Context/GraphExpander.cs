using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Expands a seed set of knowledge rules by adding their graph neighbours via multi-hop
/// breadth-first traversal (default depth 2), deduplicating by rule ID and bounding the total
/// number of added neighbours. Respects user scope.
/// </summary>
internal sealed class GraphExpander
{
    /// <summary>Default number of hops to expand outward from the seed rules.</summary>
    private const int DefaultMaxDepth = 2;

    /// <summary>Upper bound on newly discovered neighbours, to prevent fan-out blow-up.</summary>
    private const int MaxNeighbors = 30;

    private readonly ICypherExecutor _cypher;

    /// <summary>
    /// Initializes a new instance of <see cref="GraphExpander"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor for graph traversal queries.</param>
    internal GraphExpander(ICypherExecutor cypher) => _cypher = cypher;

    /// <summary>
    /// Returns the seed rules plus their up-to-<paramref name="maxDepth"/>-hop neighbours,
    /// respecting user scope. Traversal is breadth-first: each level queries the 1-hop neighbours of
    /// the current frontier, and newly discovered rules form the next frontier.
    /// </summary>
    /// <param name="seedRules">The top-ranked rules to expand from.</param>
    /// <param name="userId">User ID for scope filtering. Null includes only global rules.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="maxDepth">Maximum number of hops to traverse (default 2).</param>
    /// <returns>Deduplicated list: the seed rules followed by newly discovered neighbours.</returns>
    internal async Task<IReadOnlyList<KnowledgeRule>> ExpandAsync(
        IReadOnlyList<KnowledgeRule> seedRules,
        string? userId,
        CancellationToken ct,
        int maxDepth = DefaultMaxDepth)
    {
        var seen = seedRules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var neighbors = new List<KnowledgeRule>();
        var frontier = seedRules.Select(r => r.Id).ToList();

        for (var depth = 0; depth < maxDepth && frontier.Count > 0 && neighbors.Count < MaxNeighbors; depth++)
        {
            var rows = await _cypher.QueryAsync(
                """
                MATCH (r:Rule)-[]-(n:Rule)
                WHERE r.id IN $frontier AND (n.ownerId IS NULL OR n.ownerId = $userId)
                RETURN DISTINCT n
                """,
                new { frontier, userId },
                ct).ConfigureAwait(false);

            var nextFrontier = new List<string>();
            foreach (var row in rows)
            {
                var neighbor = NodeMapper.MapRowObject(row.TryGetValue("n", out var n) ? n : null);
                if (neighbor.Id == "unknown" || !seen.Add(neighbor.Id))
                    continue;

                neighbors.Add(neighbor);
                nextFrontier.Add(neighbor.Id);
                if (neighbors.Count >= MaxNeighbors)
                    break;
            }

            frontier = nextFrontier;
        }

        return [.. seedRules, .. neighbors];
    }
}
