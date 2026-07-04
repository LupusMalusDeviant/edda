using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Semantic persistence contract for the knowledge graph's rule layer (ADR-0013): the read operations the
/// graph API needs, expressed as intent rather than Cypher, so any backend — Cypher-based (Neo4j/Memgraph)
/// or, later, a native store — can implement them. Tenant scoping is ambient (via
/// <see cref="IIdentityContext"/>), mirroring the rest of the graph layer (C1). This is the first slice of
/// the pluggable-persistence seam; the write operations follow in a later slice.
/// </summary>
public interface IGraphStore
{
    /// <summary>Returns a single rule by id, scoped to the caller (owner + ambient tenant), excluding soft-deleted rules.</summary>
    /// <param name="ruleId">The rule's unique id.</param>
    /// <param name="userId">User scope; null returns only global rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule, or null if not found or out of scope.</returns>
    Task<KnowledgeRule?> GetRuleAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns rules filtered by optional domain/type/tag, scoped to the caller, excluding soft-deleted rules.</summary>
    /// <param name="domain">Filter by domain; null = all.</param>
    /// <param name="type">Filter by type; null = all.</param>
    /// <param name="tag">Filter by tag; null = no tag filter.</param>
    /// <param name="userId">User scope for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching rules visible to the caller.</returns>
    Task<IReadOnlyList<KnowledgeRule>> GetRulesAsync(
        string? domain = null,
        string? type = null,
        string? tag = null,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the structural head rules (excluding nested file-level leaves), scoped to the caller.</summary>
    /// <param name="userId">User scope for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The head rules visible to the caller.</returns>
    Task<IReadOnlyList<KnowledgeRule>> GetRuleHeadsAsync(
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the 1-hop neighbours of a rule across all relationships, scoped to the caller.</summary>
    /// <param name="ruleId">The centre rule's id.</param>
    /// <param name="userId">User scope for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The neighbouring rules visible to the caller.</returns>
    Task<IReadOnlyList<KnowledgeRule>> FindNeighborsAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the distinct owner ids of rules of the given type.</summary>
    /// <param name="type">The rule type to inspect.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Distinct non-null owner ids.</returns>
    Task<IReadOnlyList<string>> ListOwnersAsync(
        string type,
        CancellationToken cancellationToken = default);
}
