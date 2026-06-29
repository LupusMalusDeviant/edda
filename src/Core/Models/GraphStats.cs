namespace Edda.Core.Models;

/// <summary>
/// Aggregate statistics about the knowledge graph, returned by IKnowledgeGraph.GetStatsAsync().
/// </summary>
public sealed record GraphStats
{
    /// <summary>Total number of rules in the graph (global + user-owned).</summary>
    public required int TotalRules { get; init; }

    /// <summary>Number of operator/system rules (OwnerId = null).</summary>
    public required int GlobalRules { get; init; }

    /// <summary>Number of user-specific rules (OwnerId != null).</summary>
    public required int UserRules { get; init; }

    /// <summary>Rule count broken down by domain name.</summary>
    public required IReadOnlyDictionary<string, int> RulesByDomain { get; init; }

    /// <summary>Rule count broken down by type (Rule, Pattern, Convention, etc.).</summary>
    public required IReadOnlyDictionary<string, int> RulesByType { get; init; }

    /// <summary>Total number of relationship edges in the graph.</summary>
    public required int TotalEdges { get; init; }

    /// <summary>Number of rules that have an associated TDK validator script.</summary>
    public required int RulesWithValidators { get; init; }

    /// <summary>Number of rules that have pre-computed embeddings cached in Neo4j.</summary>
    public required int RulesWithEmbeddings { get; init; }

    /// <summary>Rules still awaiting embedding (no chunks yet, under the retry cap).</summary>
    public int PendingEmbeddingCount { get; init; }

    /// <summary>Rules that repeatedly failed to embed and hit the retry cap (skipped by the backfill).</summary>
    public int FailedEmbeddingCount { get; init; }

    /// <summary>Repository/upload heads that already have head-vector centroids (ADR-0009 stage 1).</summary>
    public int HeadsWithVectors { get; init; }

    /// <summary>Total repository/upload heads eligible for head vectors.</summary>
    public int TotalHeads { get; init; }

    // ── Live embedding rebuild status ───────────────────────────────────────

    /// <summary>True while a background embedding rebuild is in progress.</summary>
    public bool EmbeddingRebuildRunning { get; init; }

    /// <summary>Total rules to embed in current rebuild cycle (0 if not running).</summary>
    public int EmbeddingRebuildTotal { get; init; }

    /// <summary>Rules already embedded in current rebuild cycle (0 if not running).</summary>
    public int EmbeddingRebuildDone { get; init; }

    /// <summary>The rule ID currently being embedded, or null if idle.</summary>
    public string? EmbeddingRebuildCurrentRule { get; init; }
}
