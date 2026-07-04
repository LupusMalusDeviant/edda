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

    /// <summary>Initializes a new instance of the <see cref="CypherGraphStore"/> class.</summary>
    /// <param name="cypher">Executor for the generated Cypher (any Cypher backend or the in-memory dev executor).</param>
    /// <param name="identity">Ambient identity supplying the current tenant (C1); null falls back to the default tenant.</param>
    public CypherGraphStore(ICypherExecutor cypher, IIdentityContext? identity = null)
    {
        _cypher = cypher;
        _identity = identity;
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
}
