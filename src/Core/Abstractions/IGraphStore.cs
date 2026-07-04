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

    /// <summary>
    /// Persists a rule node and its relationship edges (the pure graph write): upserts the node, stamps the
    /// ambient tenant and a first-seen <c>validFrom</c>, and temporally replaces the typed edges. Embedding
    /// is a separate concern owned by the caller and is not performed here.
    /// </summary>
    /// <param name="rule">The rule to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertRuleGraphAsync(
        KnowledgeRule rule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a rule (E10): stamps <c>deletedAt</c>/<c>deletedBy</c> and closes its validity so it
    /// drops out of context compilation but stays restorable. Authorization is the caller's responsibility.
    /// </summary>
    /// <param name="ruleId">The rule to soft-delete.</param>
    /// <param name="userId">The acting user, recorded as <c>deletedBy</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteRuleGraphAsync(
        string ruleId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes a rule subtree — the root plus every rule whose id starts with one of the given prefixes —
    /// together with their chunks. Authorization and prefix resolution are the caller's responsibility.
    /// </summary>
    /// <param name="rootId">The subtree root id.</param>
    /// <param name="prefixes">Id prefixes that define the subtree membership.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rules deleted.</returns>
    Task<int> DeleteSubtreeGraphAsync(
        string rootId,
        IReadOnlyList<string> prefixes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the validity of rules that a live rule supersedes (C9): marks them <c>validUntil</c>/
    /// <c>invalidatedBy</c> and ends their open edges, except the documenting incoming SUPERSEDES edge.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateSupersededAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the candidate rules for context compilation: in scope for the caller and ambient tenant, with
    /// tool-domain gating (a <c>tools.*</c> rule is included only when its domain is a resolved toolbox),
    /// temporal validity, and optional leaf pre-pruning to the given id prefixes. The retrieval strategy
    /// (which toolboxes/prefixes) is decided by the caller.
    /// </summary>
    /// <param name="userId">User scope; null returns only global rules.</param>
    /// <param name="toolboxes">Resolved <c>tools.*</c> domains to admit; other tool domains are gated out.</param>
    /// <param name="prefixes">Id prefixes that git/upload leaves are restricted to; empty = no pre-pruning.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The in-scope, temporally valid candidate rules.</returns>
    Task<IReadOnlyList<KnowledgeRule>> GetCompilationRulesAsync(
        string? userId,
        IReadOnlyList<string> toolboxes,
        IReadOnlyList<string> prefixes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct 1-hop neighbours of a frontier of rules reached via still-open edges (C9),
    /// scoped to the caller and ambient tenant. Used for breadth-first activation expansion.
    /// </summary>
    /// <param name="frontier">The source rule ids to expand from.</param>
    /// <param name="userId">User scope; null returns only global neighbours.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The neighbouring rules reachable via open edges.</returns>
    Task<IReadOnlyList<KnowledgeRule>> FindOpenNeighborsAsync(
        IReadOnlyList<string> frontier,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
