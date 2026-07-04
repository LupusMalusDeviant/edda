using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Graph;

/// <summary>
/// Cypher-backed <see cref="IGraphStore"/> (ADR-0013): implements the graph read operations by building the
/// Cypher the AKG layer has always used and executing it through <see cref="ICypherExecutor"/> — so it works
/// over any Cypher backend (Neo4j/Memgraph) and, unchanged, over the in-memory dev executor. Tenant scoping
/// is ambient via <see cref="IIdentityContext"/> (C1). This slice covers the read operations; writes remain
/// in the knowledge-graph orchestrator for now.
/// </summary>
internal sealed class CypherGraphStore : IGraphStore
{
    private readonly ICypherExecutor _cypher;
    private readonly IIdentityContext? _identity;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="CypherGraphStore"/> class.</summary>
    /// <param name="cypher">Executor for the generated Cypher (any Cypher backend or the in-memory dev executor).</param>
    /// <param name="identity">Ambient identity supplying the current tenant (C1); null falls back to the default tenant.</param>
    /// <param name="timeProvider">Time source for temporal-validity timestamps (validFrom/validUntil/deletedAt); null uses the system clock.</param>
    public CypherGraphStore(ICypherExecutor cypher, IIdentityContext? identity = null, TimeProvider? timeProvider = null)
    {
        _cypher = cypher;
        _identity = identity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The ambient tenant of the current context (read per call, never cached).</summary>
    private string Tenant => _identity?.TenantId ?? Tenants.DefaultTenantId;

    /// <inheritdoc />
    public async Task<KnowledgeRule?> GetRuleAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // E10: soft-deleted rules are invisible to the regular API. C1: missing tenantId = default tenant.
        var rows = await _cypher.QueryAsync(
            "MATCH (r:Rule {id: $ruleId}) WHERE (r.ownerId IS NULL OR r.ownerId = $userId) " +
            "AND r.deletedAt IS NULL AND coalesce(r.tenantId, 'default') = $tenantId RETURN r",
            new { ruleId, userId, tenantId = Tenant },
            cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0) return null;

        var mapped = NodeMapper.MapRowObject(rows[0].TryGetValue("r", out var r) ? r : null);
        return mapped.Id == "unknown" ? null : mapped;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeRule>> GetRulesAsync(
        string? domain = null,
        string? type = null,
        string? tag = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var query = BuildGetRulesQuery(domain, type, tag);
        var rows = await _cypher.QueryAsync(
            query,
            new { domain, type, tag, userId, tenantId = Tenant },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("r", out var r) ? r : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeRule>> GetRuleHeadsAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Exclude file-level leaves (git:<repo>:<path> / upload:<source>:<file>) so the graph renders the
        // structural hierarchy + standalone rules instead of every ingested file.
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE (r.ownerId IS NULL OR r.ownerId = $userId)
              AND r.deletedAt IS NULL
              AND coalesce(r.tenantId, 'default') = $tenantId
              AND NOT ((r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:') AND size(split(r.id, ':')) >= 3)
            RETURN r
            """,
            new { userId, tenantId = Tenant },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("r", out var r) ? r : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeRule>> FindNeighborsAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (r:Rule {id: $ruleId})-[]-(n:Rule) WHERE (n.ownerId IS NULL OR n.ownerId = $userId) " +
            "AND coalesce(n.tenantId, 'default') = $tenantId RETURN n",
            new { ruleId, userId, tenantId = Tenant },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("n", out var n) ? n : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListOwnersAsync(
        string type,
        CancellationToken cancellationToken = default)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (r:Rule) WHERE r.type = $type AND r.ownerId IS NOT NULL RETURN DISTINCT r.ownerId AS ownerId",
            new { type },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => row.TryGetValue("ownerId", out var o) ? o?.ToString() : null)
            .Where(o => !string.IsNullOrEmpty(o))
            .Select(o => o!)
            .ToList();
    }

    /// <inheritdoc />
    public async Task UpsertRuleGraphAsync(KnowledgeRule rule, CancellationToken cancellationToken = default)
    {
        var implies = rule.RelatesTo?.Implies.ToArray() ?? [];
        var conflictsWith = rule.RelatesTo?.ConflictsWith.ToArray() ?? [];
        var exceptionFor = rule.RelatesTo?.ExceptionFor.ToArray() ?? [];
        var requires = rule.RelatesTo?.Requires.ToArray() ?? [];
        var supersedes = rule.RelatesTo?.Supersedes.ToArray() ?? [];
        var related = rule.RelatesTo?.Related.ToArray() ?? [];
        // B5: persist the rule's trigger concepts so the keyword scorer's concept branch (and the
        // co-occurrence query expansion) work on graph-loaded rules.
        var concepts = rule.WhenRelevant?.DetectedConcepts.ToArray() ?? [];

        await _cypher.ExecuteAsync(
            """
            MERGE (r:Rule {id: $id})
            SET r.type = $type,
                r.domain = $domain,
                r.priority = $priority,
                r.body = $body,
                r.tags = $tags,
                r.ownerId = $ownerId,
                r.tenantId = $tenantId,
                r.implies = $implies,
                r.conflictsWith = $conflictsWith,
                r.exceptionFor = $exceptionFor,
                r.requires = $requires,
                r.supersedes = $supersedes,
                r.related = $related,
                r.concepts = $concepts,
                r.validatorType = $validatorType,
                r.validatorPrompt = $validatorPrompt,
                r.chunkStyle = $chunkStyle,
                r.validFrom = coalesce(r.validFrom, $now)
            """,
            new
            {
                id = rule.Id,
                type = rule.Type,
                domain = rule.Domain,
                priority = rule.Priority.ToString(),
                body = rule.Body,
                tags = rule.Tags.ToArray(),
                ownerId = rule.OwnerId,
                // C1: the ambient context stamps the tenant — never the model field (anti-spoofing,
                // the rule-6 analogy: scoping comes from the context, not from arguments).
                tenantId = Tenant,
                implies,
                conflictsWith,
                exceptionFor,
                requires,
                supersedes,
                related,
                concepts,
                validatorType = rule.ValidatorType,
                validatorPrompt = rule.ValidatorPrompt,
                chunkStyle = rule.ChunkStyle,
                now = _timeProvider.GetUtcNow().ToString("O"),
            },
            cancellationToken).ConfigureAwait(false);

        // Create relationship edges for graph traversal (C9 temporal replace).
        await UpsertEdgesAsync(rule.Id, "IMPLIES", implies, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "CONFLICTS_WITH", conflictsWith, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "EXCEPTION_FOR", exceptionFor, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "REQUIRES", requires, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "SUPERSEDES", supersedes, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "RELATED", related, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteRuleGraphAsync(string ruleId, string userId, CancellationToken cancellationToken = default)
    {
        // E10 soft delete: mark instead of removing. validUntil (coalesced, so an earlier supersede
        // timestamp survives) drops it from context compilation; deletedAt hides it from active views and
        // lists it in the recycle bin. Chunks stay for a lossless restore and are removed on purge.
        var now = _timeProvider.GetUtcNow().ToString("O");
        await _cypher.ExecuteAsync(
            """
            MATCH (r:Rule {id: $ruleId})
            SET r.deletedAt = $now,
                r.deletedBy = $userId,
                r.validUntil = coalesce(r.validUntil, $now)
            """,
            new { ruleId, userId, now },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteSubtreeGraphAsync(string rootId, IReadOnlyList<string> prefixes, CancellationToken cancellationToken = default)
    {
        var prefixArray = prefixes.ToArray();
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE r.id = $rootId OR ANY(p IN $prefixes WHERE r.id STARTS WITH p)
            RETURN count(r) AS n
            """,
            new { rootId, prefixes = prefixArray },
            cancellationToken).ConfigureAwait(false);
        var deleted = rows.Count > 0 && rows[0].TryGetValue("n", out var n) ? ToInt(n) : 0;

        await _cypher.ExecuteAsync(
            """
            MATCH (r:Rule)
            WHERE r.id = $rootId OR ANY(p IN $prefixes WHERE r.id STARTS WITH p)
            OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
            DETACH DELETE r, c
            """,
            new { rootId, prefixes = prefixArray },
            cancellationToken).ConfigureAwait(false);

        return deleted;
    }

    /// <inheritdoc />
    public async Task InvalidateSupersededAsync(CancellationToken cancellationToken = default)
    {
        // C9: a superseded fact's relationships end with it — close all of its open edges too, except the
        // incoming SUPERSEDES edge, which documents the supersession and stays valid from now on.
        var now = _timeProvider.GetUtcNow().ToString("O");
        await _cypher.ExecuteAsync(
            """
            MATCH (newer:Rule)
            WHERE newer.validUntil IS NULL AND newer.supersedes IS NOT NULL
            UNWIND newer.supersedes AS olderId
            MATCH (older:Rule {id: olderId})
            WHERE older.validUntil IS NULL AND older.id <> newer.id
            SET older.validUntil = $now, older.invalidatedBy = newer.id
            WITH DISTINCT older
            OPTIONAL MATCH (older)-[e]-(other:Rule)
            WHERE e.validUntil IS NULL
              AND NOT (type(e) = 'SUPERSEDES' AND endNode(e) = older)
            SET e.validUntil = $now
            """,
            new { now },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeRule>> GetCompilationRulesAsync(
        string? userId,
        IReadOnlyList<string> toolboxes,
        IReadOnlyList<string> prefixes,
        CancellationToken cancellationToken = default)
    {
        // Owner/tenant scope + tool-domain gating (tools.* only when the domain is a resolved toolbox) +
        // temporal validity + optional leaf pre-pruning by id prefix. Byte-identical to the compiler's
        // former inline query — the retrieval strategy (which toolboxes/prefixes) stays in the compiler.
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE (r.ownerId IS NULL OR r.ownerId = $userId)
              AND coalesce(r.tenantId, 'default') = $tenantId
              AND (NOT r.domain STARTS WITH 'tools.' OR r.domain IN $toolboxes)
              AND (r.validUntil IS NULL OR r.validUntil > $now)
              AND (size($prefixes) = 0
                   OR NOT (r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:')
                   OR any(p IN $prefixes WHERE r.id STARTS WITH p))
            RETURN r
            """,
            new
            {
                userId,
                tenantId = Tenant,
                toolboxes,
                now = _timeProvider.GetUtcNow().ToString("O"),
                prefixes,
            },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("r", out var r) ? r : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeRule>> FindOpenNeighborsAsync(
        IReadOnlyList<string> frontier,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Multi-source 1-hop neighbours reached via still-open edges (C9): validUntil null or in the
        // future. The BFS loop, dedup and fan-out bounding stay in the caller (GraphExpander).
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)-[e]-(n:Rule)
            WHERE r.id IN $frontier AND (n.ownerId IS NULL OR n.ownerId = $userId)
              AND coalesce(n.tenantId, 'default') = $tenantId
              AND (e.validUntil IS NULL OR e.validUntil > $now)
            RETURN DISTINCT n
            """,
            new { frontier, userId, now = _timeProvider.GetUtcNow().ToString("O"), tenantId = Tenant },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("n", out var n) ? n : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    private static string BuildGetRulesQuery(string? domain, string? type, string? tag)
    {
        var conditions = new List<string>
        {
            "(r.ownerId IS NULL OR r.ownerId = $userId)",
            // E10: active listings never show soft-deleted rules.
            "r.deletedAt IS NULL",
            // C1: tenant isolation (missing property = default tenant).
            "coalesce(r.tenantId, 'default') = $tenantId",
        };

        if (domain != null) conditions.Add("r.domain = $domain");
        if (type != null) conditions.Add("r.type = $type");
        if (tag != null) conditions.Add("$tag IN r.tags");

        return $"MATCH (r:Rule) WHERE {string.Join(" AND ", conditions)} RETURN r";
    }

    private async Task UpsertEdgesAsync(string sourceId, string relType, string[] targetIds, CancellationToken ct)
    {
        // C9: temporal replace instead of delete+recreate, in a single round-trip. Open edges of this type
        // whose target is no longer declared are closed (validUntil = now), keeping the relationship
        // history; declared targets are merged with a first-seen validFrom (ON CREATE) and re-opened when
        // previously closed (SET validUntil = null). An empty target list still closes all remaining open
        // edges (UNWIND over an empty list yields no rows), and SET on a NULL entity is a Cypher no-op.
        // relType is a fixed internal constant (never user input), so interpolating it is safe.
        var now = _timeProvider.GetUtcNow().ToString("O");
        await _cypher.ExecuteAsync(
            "MATCH (s:Rule {id: $sourceId}) " +
            $"OPTIONAL MATCH (s)-[stale:{relType}]->(t0:Rule) " +
            "WHERE stale.validUntil IS NULL AND NOT t0.id IN $targetIds " +
            "SET stale.validUntil = $now " +
            "WITH DISTINCT s " +
            "UNWIND $targetIds AS targetId " +
            "MATCH (t:Rule {id: targetId}) " +
            $"MERGE (s)-[e:{relType}]->(t) " +
            "ON CREATE SET e.validFrom = $now " +
            "SET e.validUntil = null",
            new { sourceId, targetIds, now },
            ct).ConfigureAwait(false);
    }

    private static int ToInt(object? value) => Convert.ToInt32(value ?? 0);
}
