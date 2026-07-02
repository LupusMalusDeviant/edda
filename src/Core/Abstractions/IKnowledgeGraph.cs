using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Abstracts the Agent Knowledge Graph (AKG). The Neo4j implementation lives in the AKG project.
/// All query methods are user-aware: userId=null returns only global/operator rules;
/// userId="abc" returns global rules plus rules owned by that user.
/// </summary>
public interface IKnowledgeGraph
{
    /// <summary>
    /// Loads all rules from the knowledge/ directory and imports them into Neo4j.
    /// Validates the graph structure (cycles, broken references).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single rule by ID, respecting user scope.
    /// </summary>
    /// <param name="ruleId">The rule's unique kebab-case identifier.</param>
    /// <param name="userId">User ID for scope filtering. Null returns only global rules.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule, or null if not found or out of scope.</returns>
    Task<KnowledgeRule?> GetRuleAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all rules, optionally filtered by domain, type, and/or tag. Respects user scope.
    /// </summary>
    /// <param name="domain">Filter by domain name. Null = all domains.</param>
    /// <param name="type">Filter by rule type. Null = all types.</param>
    /// <param name="tag">Filter by tag. Null = no tag filter.</param>
    /// <param name="userId">User ID for scope filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Filtered list of rules visible to the given user.</returns>
    Task<IReadOnlyList<KnowledgeRule>> GetRulesAsync(
        string? domain = null,
        string? type = null,
        string? tag = null,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only the "head" rules for the graph overview: structural nodes (roots, hosts, groups,
    /// repositories, upload sources) and standalone rules — excluding the file-level leaf nodes whose ids
    /// nest beneath a repository or upload source (<c>git:&lt;repo&gt;:&lt;path&gt;</c>,
    /// <c>upload:&lt;source&gt;:&lt;file&gt;</c>). Lets the UI render a large knowledge base as a collapsed
    /// hierarchy instead of tens of thousands of file nodes. Respects user scope.
    /// </summary>
    /// <param name="userId">User ID for scope filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The head rules visible to the given user.</returns>
    Task<IReadOnlyList<KnowledgeRule>> GetRuleHeadsAsync(
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all 1-hop neighbor rules for a given rule across all relationship types.
    /// </summary>
    /// <param name="ruleId">The source rule ID.</param>
    /// <param name="userId">User ID for scope filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Neighboring rules connected by any relationship.</returns>
    Task<IReadOnlyList<KnowledgeRule>> FindNeighborsAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the context compilation pipeline for the given task context.
    /// </summary>
    /// <param name="taskContext">Task context including user message and extracted concepts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compiled context with active rules, implied rules, conflicts, and formatted output.</returns>
    Task<ContextResult> CompileContextAsync(
        TaskContext taskContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a rule. If OwnerId is set, the rule is user-specific.
    /// </summary>
    /// <param name="rule">The rule to create or update (matched by Id).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted rule with any system-assigned defaults applied.</returns>
    Task<KnowledgeRule> UpsertRuleAsync(
        KnowledgeRule rule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enters bulk-ingestion mode for the lifetime of the returned scope. While a scope is held,
    /// <see cref="UpsertRuleAsync"/> skips its synchronous inline embedding so a large import is not
    /// bottlenecked on the embedding provider. Disposing the outermost scope leaves bulk mode and kicks off
    /// a single background embedding rebuild covering the imported rules. Reference-counted — nested scopes
    /// are safe.
    /// </summary>
    /// <returns>A disposable scope; dispose it when the bulk import finishes.</returns>
    IDisposable BeginBulkIngestion();

    /// <summary>
    /// Deletes a rule. Non-admin users can only delete their own rules.
    /// </summary>
    /// <param name="ruleId">The rule ID to delete.</param>
    /// <param name="userId">The requesting user's ID.</param>
    /// <param name="isAdmin">True if the user has operator/admin privileges.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown if userId != rule.OwnerId and isAdmin is false.
    /// </exception>
    Task DeleteRuleAsync(
        string ruleId,
        string userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a head rule together with its entire subtree — every descendant whose id nests beneath it
    /// (e.g. a repository node plus all of its file nodes). Deleting the <c>git-knowledge</c> or
    /// <c>uploads</c> root removes that whole ingested branch. Non-admins may only delete their own rules.
    /// </summary>
    /// <param name="rootId">The head rule id whose subtree is removed.</param>
    /// <param name="userId">The requesting user's ID.</param>
    /// <param name="isAdmin">True if the user has operator/admin privileges.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rules deleted.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if the user may not delete the head rule.</exception>
    Task<int> DeleteSubtreeAsync(
        string rootId,
        string userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns aggregate statistics about the knowledge graph.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Graph statistics including rule counts, domain distribution, and edge counts.</returns>
    Task<GraphStats> GetStatsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct owner ids of all user-owned rules of the given <paramref name="type"/>
    /// (e.g. every user who has episodic memories). Global/operator rules (no owner) are excluded.
    /// Used by background maintenance that must run per user without a request-scoped identity.
    /// </summary>
    /// <param name="type">The rule type to enumerate owners for (e.g. <c>Memory</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The distinct, non-null owner ids; empty when no owned rules of that type exist.</returns>
    Task<IReadOnlyList<string>> ListOwnersAsync(
        string type,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all existing <c>:WorldKnowledge</c> nodes and reloads them from the
    /// <c>knowledge/world/</c> directory. Admin-only operation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of WorldKnowledge nodes successfully loaded.</returns>
    Task<int> ReloadWorldKnowledgeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds embeddings for all rules that have missing or stale embeddings.
    /// Delegates to the embedding cache; no-op if the embedding service is unavailable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RebuildEmbeddingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <c>validUntil</c> and <c>invalidatedBy</c> on rules that are superseded by a
    /// currently-valid rule (via the <c>supersedes</c> relation), so they drop out of context
    /// compilation while remaining in the graph as history. Idempotent; no-op for already-invalidated
    /// rules. Typically run after seeding and after knowledge compilation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateSupersededRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all stored rule embeddings and rebuilds them from scratch. Use after an embedding-model
    /// change, where cached vectors have the wrong dimensions/model and the incremental
    /// <see cref="RebuildEmbeddingsAsync"/> would skip them. The embedding step is a no-op when the
    /// embedding service is unavailable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResetAndRebuildEmbeddingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation of an in-progress embedding rebuild (no-op if none is running). Lets the UI
    /// abort a long or stuck rebuild instead of waiting it out.
    /// </summary>
    void CancelEmbeddingRebuild();
}
