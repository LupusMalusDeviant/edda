namespace Edda.Core.Models;

/// <summary>
/// Graph-derived rule statistics (pure counts) — the persistence-layer subset of <see cref="GraphStats"/>,
/// produced by <see cref="Edda.Core.Abstractions.IGraphStore"/>. Embedding/vector coverage and rebuild
/// progress are composed on top by the knowledge-graph orchestrator.
/// </summary>
/// <param name="TotalRules">Total rule count.</param>
/// <param name="GlobalRules">Rules with no owner (global/system).</param>
/// <param name="UserRules">Rules with an owner (user-scoped).</param>
/// <param name="RulesWithValidators">Rules that carry a validator script.</param>
/// <param name="TotalEdges">Relationship count excluding <c>HAS_CHUNK</c>.</param>
/// <param name="RulesWithEmbeddings">Distinct rules that have at least one embedded chunk.</param>
/// <param name="RulesByDomain">Rule count per domain.</param>
/// <param name="RulesByType">Rule count per type.</param>
public sealed record GraphRuleStats(
    int TotalRules,
    int GlobalRules,
    int UserRules,
    int RulesWithValidators,
    int TotalEdges,
    int RulesWithEmbeddings,
    IReadOnlyDictionary<string, int> RulesByDomain,
    IReadOnlyDictionary<string, int> RulesByType);
