using Edda.AKG.Context;
using Edda.AKG.Embeddings;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace Edda.AKG.Graph;

/// <summary>
/// Neo4j-backed implementation of <see cref="IKnowledgeGraph"/>.
/// Delegates context compilation to <see cref="ContextCompiler"/>
/// and rule loading to <see cref="RuleLoader"/>.
/// </summary>
public sealed class Neo4jKnowledgeGraph : IKnowledgeGraph
{
    private const string KnowledgeDirectory = "knowledge";
    private const string WorldKnowledgeDirectory = "knowledge/world";

    private readonly ICypherExecutor _cypher;
    private readonly IContextCompiler _contextCompiler;
    private readonly IRuleLoader _ruleLoader;
    private readonly IWorldKnowledgeSeeder _worldKnowledgeSeeder;
    private readonly INeo4jEmbeddingCache _embeddingCache;
    private readonly IHeadVectorStore _headVectorStore;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<Neo4jKnowledgeGraph> _logger;

    // >0 while a bulk ingestion is in progress; suppresses per-upsert inline embedding (see BeginBulkIngestion).
    private int _bulkIngestionDepth;

    /// <summary>
    /// Initializes a new instance of <see cref="Neo4jKnowledgeGraph"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor for all graph operations.</param>
    /// <param name="contextCompiler">Context compilation pipeline.</param>
    /// <param name="ruleLoader">Rule file loader for reload operations.</param>
    /// <param name="worldKnowledgeSeeder">Seeder for <c>:WorldKnowledge</c> nodes.</param>
    /// <param name="embeddingCache">Embedding cache for automatic embedding generation.</param>
    /// <param name="headVectorStore">Head-vector store, queried for stage-1 coverage statistics.</param>
    /// <param name="fileSystem">File system abstraction for path resolution.</param>
    /// <param name="timeProvider">Time provider for temporal-validity timestamps.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    internal Neo4jKnowledgeGraph(
        ICypherExecutor cypher,
        IContextCompiler contextCompiler,
        IRuleLoader ruleLoader,
        IWorldKnowledgeSeeder worldKnowledgeSeeder,
        INeo4jEmbeddingCache embeddingCache,
        IHeadVectorStore headVectorStore,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<Neo4jKnowledgeGraph> logger)
    {
        _cypher = cypher;
        _contextCompiler = contextCompiler;
        _ruleLoader = ruleLoader;
        _worldKnowledgeSeeder = worldKnowledgeSeeder;
        _embeddingCache = embeddingCache;
        _headVectorStore = headVectorStore;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var directory = _fileSystem.GetFullPath(KnowledgeDirectory);
        var count = await _ruleLoader.LoadFromDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("AKG reloaded: {Count} rules imported | {Component}", count, "AKG");
    }

    /// <inheritdoc/>
    public async Task<KnowledgeRule?> GetRuleAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (r:Rule {id: $ruleId}) WHERE r.ownerId IS NULL OR r.ownerId = $userId RETURN r",
            new { ruleId, userId },
            cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0) return null;

        var mapped = NodeMapper.MapRowObject(rows[0].TryGetValue("r", out var r) ? r : null);
        return mapped.Id == "unknown" ? null : mapped;
    }

    /// <inheritdoc/>
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
            new { domain, type, tag, userId },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("r", out var r) ? r : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeRule>> GetRuleHeadsAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Exclude file-level leaves (git:<repo>:<path> / upload:<source>:<file>) so the graph renders the
        // structural hierarchy + standalone rules instead of every ingested file. The leaves are loaded
        // lazily per head, keeping a large knowledge base (tens of thousands of files) responsive.
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE (r.ownerId IS NULL OR r.ownerId = $userId)
              AND NOT ((r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:') AND size(split(r.id, ':')) >= 3)
            RETURN r
            """,
            new { userId },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("r", out var r) ? r : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeRule>> FindNeighborsAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (r:Rule {id: $ruleId})-[]-(n:Rule) WHERE n.ownerId IS NULL OR n.ownerId = $userId RETURN n",
            new { ruleId, userId },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.MapRowObject(row.TryGetValue("n", out var n) ? n : null))
            .Where(r => r.Id != "unknown")
            .ToList();
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public Task<ContextResult> CompileContextAsync(
        TaskContext taskContext,
        CancellationToken cancellationToken = default)
        => _contextCompiler.CompileAsync(taskContext, cancellationToken);

    /// <inheritdoc/>
    public async Task<KnowledgeRule> UpsertRuleAsync(
        KnowledgeRule rule,
        CancellationToken cancellationToken = default)
    {
        var implies = rule.RelatesTo?.Implies.ToArray() ?? [];
        var conflictsWith = rule.RelatesTo?.ConflictsWith.ToArray() ?? [];
        var exceptionFor = rule.RelatesTo?.ExceptionFor.ToArray() ?? [];
        var requires = rule.RelatesTo?.Requires.ToArray() ?? [];
        var supersedes = rule.RelatesTo?.Supersedes.ToArray() ?? [];
        var related = rule.RelatesTo?.Related.ToArray() ?? [];

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
                tenantId = rule.TenantId,
                implies,
                conflictsWith,
                exceptionFor,
                requires,
                supersedes,
                related,
                chunkStyle = rule.ChunkStyle,
                now = _timeProvider.GetUtcNow().ToString("O"),
            },
            cancellationToken).ConfigureAwait(false);

        // Create relationship edges for graph traversal
        await UpsertEdgesAsync(rule.Id, "IMPLIES", implies, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "CONFLICTS_WITH", conflictsWith, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "EXCEPTION_FOR", exceptionFor, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "REQUIRES", requires, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "SUPERSEDES", supersedes, cancellationToken).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "RELATED", related, cancellationToken).ConfigureAwait(false);

        // Skip inline embedding while a full rebuild is running or during a bulk ingestion — otherwise an
        // upsert (e.g. an import) would block on the shared, possibly slow embedding provider. The rebuild
        // covers all rules anyway; a bulk ingestion triggers one rebuild when its scope closes.
        if (!_embeddingCache.IsRebuilding && Volatile.Read(ref _bulkIngestionDepth) == 0)
        {
            try
            {
                await _embeddingCache.EmbedSingleAsync(rule.Id, rule.Body, rule.ChunkStyle, cancellationToken).ConfigureAwait(false);
            }
            catch (Core.Exceptions.ProviderException ex)
            {
                // Embeddings are best-effort — a degraded embedding provider must never block rule upsert.
                _logger.LogWarning(
                    "Embedding skipped for rule '{RuleId}' — provider unavailable ({Message}) | AKG",
                    rule.Id, ex.Message);
            }
        }
        else
        {
            // Inline embedding was skipped (bulk import or active rebuild). Clear the body hash so the next
            // rebuild re-embeds this rule even if its content changed — the rebuild only selects rules whose
            // body hash is missing or that have no chunks.
            await _cypher.ExecuteAsync(
                "MATCH (r:Rule {id: $id}) REMOVE r.bodyHash",
                new { id = rule.Id },
                cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("Upserted rule '{RuleId}' (domain: {Domain}) | {Component}", rule.Id, rule.Domain, "AKG");
        return rule;
    }

    /// <inheritdoc/>
    public IDisposable BeginBulkIngestion()
    {
        Interlocked.Increment(ref _bulkIngestionDepth);
        return new BulkIngestionScope(this);
    }

    /// <summary>
    /// Leaves bulk-ingestion mode. When the outermost scope closes, embeds everything imported during the
    /// bulk window in one background pass so the import call itself returns promptly.
    /// </summary>
    private void EndBulkIngestion()
    {
        if (Interlocked.Decrement(ref _bulkIngestionDepth) != 0)
            return;

        // Fire-and-forget — the rebuild handles its own errors and reports progress to the activity tracker.
        _ = Task.Run(async () =>
        {
            try
            {
                await RebuildEmbeddingsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Post-import embedding rebuild failed — semantic search may be degraded | {Component}", "AKG");
            }
        });
    }

    /// <summary>Reference-counted scope returned by <see cref="BeginBulkIngestion"/>.</summary>
    private sealed class BulkIngestionScope(Neo4jKnowledgeGraph graph) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                graph.EndBulkIngestion();
        }
    }

    private async Task UpsertEdgesAsync(string sourceId, string relType, string[] targetIds, CancellationToken ct)
    {
        if (targetIds.Length == 0) return;

        // Replace all edges of this relation type from the source in a single round-trip: delete the
        // existing ones, then MERGE an edge to each target via UNWIND. Previously this issued one MERGE
        // query per target (N+1 round-trips: 100 relations = 100 queries). relType is a fixed internal
        // constant (never user input), so interpolating it into the query is safe.
        await _cypher.ExecuteAsync(
            "MATCH (s:Rule {id: $sourceId}) " +
            $"OPTIONAL MATCH (s)-[e:{relType}]->() " +
            "DELETE e " +
            "WITH DISTINCT s " +
            "UNWIND $targetIds AS targetId " +
            "MATCH (t:Rule {id: targetId}) " +
            $"MERGE (s)-[:{relType}]->(t)",
            new { sourceId, targetIds },
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteRuleAsync(
        string ruleId,
        string userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(ruleId, userId, cancellationToken).ConfigureAwait(false);
        if (rule is null) return;

        if (!isAdmin && rule.OwnerId != userId)
            throw new UnauthorizedAccessException(
                $"User '{userId}' is not authorized to delete rule '{ruleId}'.");

        await _cypher.ExecuteAsync(
            """
            MATCH (r:Rule {id: $ruleId})
            OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
            DETACH DELETE r, c
            """,
            new { ruleId },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Deleted rule '{RuleId}' by user '{UserId}' | {Component}", ruleId, userId, "AKG");
    }

    /// <inheritdoc/>
    public async Task<int> DeleteSubtreeAsync(
        string rootId,
        string userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        var root = await GetRuleAsync(rootId, userId, cancellationToken).ConfigureAwait(false);
        if (root is null) return 0;

        if (!isAdmin && root.OwnerId != userId)
            throw new UnauthorizedAccessException(
                $"User '{userId}' is not authorized to delete rule '{rootId}'.");

        // Most heads nest their descendants by id prefix (repo -> its files). The two ingested branch
        // roots aggregate nodes that don't share their prefix, so they map to the whole branch.
        var prefixes = rootId switch
        {
            "git-knowledge" => new[] { "git:", "git-host:", "git-group:" },
            "uploads" => new[] { "upload:" },
            _ => new[] { rootId + ":" },
        };

        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE r.id = $rootId OR ANY(p IN $prefixes WHERE r.id STARTS WITH p)
            RETURN count(r) AS n
            """,
            new { rootId, prefixes },
            cancellationToken).ConfigureAwait(false);
        var deleted = rows.Count > 0 && rows[0].TryGetValue("n", out var n) ? ToInt(n) : 0;

        await _cypher.ExecuteAsync(
            """
            MATCH (r:Rule)
            WHERE r.id = $rootId OR ANY(p IN $prefixes WHERE r.id STARTS WITH p)
            OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
            DETACH DELETE r, c
            """,
            new { rootId, prefixes },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Deleted subtree '{RootId}' ({Count} rules) by user '{UserId}' | {Component}",
            rootId, deleted, userId, "AKG");
        return deleted;
    }

    /// <inheritdoc/>
    public Task<int> ReloadWorldKnowledgeAsync(CancellationToken cancellationToken = default)
        => _worldKnowledgeSeeder.ReloadAsync(WorldKnowledgeDirectory, cancellationToken);

    /// <inheritdoc/>
    public async Task<GraphStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var statsRows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            RETURN
                count(r) AS total,
                sum(CASE WHEN r.ownerId IS NULL THEN 1 ELSE 0 END) AS globalRules,
                sum(CASE WHEN r.ownerId IS NOT NULL THEN 1 ELSE 0 END) AS userRules,
                sum(CASE WHEN r.validatorScript IS NOT NULL THEN 1 ELSE 0 END) AS withValidator
            """,
            ct: cancellationToken).ConfigureAwait(false);

        var edgeRows = await _cypher.QueryAsync(
            "MATCH ()-[e]->() WHERE type(e) <> 'HAS_CHUNK' RETURN count(e) AS edges",
            ct: cancellationToken).ConfigureAwait(false);

        // Documents with at least one embedded chunk (chunks are hidden; count their distinct parents).
        var embeddedRows = await _cypher.QueryAsync(
            "MATCH (c:RuleChunk) RETURN count(DISTINCT c.parentId) AS withEmbedding",
            ct: cancellationToken).ConfigureAwait(false);

        var domainRows = await _cypher.QueryAsync(
            "MATCH (r:Rule) RETURN r.domain AS domain, count(r) AS cnt",
            ct: cancellationToken).ConfigureAwait(false);

        var typeRows = await _cypher.QueryAsync(
            "MATCH (r:Rule) RETURN r.type AS type, count(r) AS cnt",
            ct: cancellationToken).ConfigureAwait(false);

        var total = 0;
        var global = 0;
        var user = 0;
        var withValidator = 0;
        var withEmbedding = 0;

        if (statsRows.Count > 0)
        {
            var r = statsRows[0];
            total = ToInt(r.TryGetValue("total", out var t) ? t : null);
            global = ToInt(r.TryGetValue("globalRules", out var g) ? g : null);
            user = ToInt(r.TryGetValue("userRules", out var u) ? u : null);
            withValidator = ToInt(r.TryGetValue("withValidator", out var wv) ? wv : null);
        }

        var edges = 0;
        if (edgeRows.Count > 0)
            edges = ToInt(edgeRows[0].TryGetValue("edges", out var e) ? e : null);

        if (embeddedRows.Count > 0)
            withEmbedding = ToInt(embeddedRows[0].TryGetValue("withEmbedding", out var we) ? we : null);

        var byDomain = domainRows
            .Where(r => r.ContainsKey("domain") && r["domain"] != null)
            .ToDictionary(
                r => r["domain"]!.ToString()!,
                r => ToInt(r.TryGetValue("cnt", out var c) ? c : null));

        var byType = typeRows
            .Where(r => r.ContainsKey("type") && r["type"] != null)
            .ToDictionary(
                r => r["type"]!.ToString()!,
                r => ToInt(r.TryGetValue("cnt", out var c) ? c : null));

        var coverage = await _embeddingCache.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        var headCoverage = await _headVectorStore.GetCoverageAsync(cancellationToken).ConfigureAwait(false);

        return new GraphStats
        {
            TotalRules = total,
            GlobalRules = global,
            UserRules = user,
            RulesByDomain = byDomain,
            RulesByType = byType,
            TotalEdges = edges,
            RulesWithValidators = withValidator,
            RulesWithEmbeddings = withEmbedding,
            PendingEmbeddingCount = coverage.Pending,
            FailedEmbeddingCount = coverage.Failed,
            HeadsWithVectors = headCoverage.HeadsWithVectors,
            TotalHeads = headCoverage.TotalHeads,
            EmbeddingRebuildRunning = _embeddingCache.IsRebuilding,
            EmbeddingRebuildTotal = _embeddingCache.TotalToEmbed,
            EmbeddingRebuildDone = _embeddingCache.EmbeddedSoFar,
            EmbeddingRebuildCurrentRule = _embeddingCache.CurrentRuleId,
        };
    }

    /// <inheritdoc/>
    public async Task RebuildEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        await _embeddingCache.RebuildAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task InvalidateSupersededRulesAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().ToString("O");
        await _cypher.ExecuteAsync(
            """
            MATCH (newer:Rule)
            WHERE newer.validUntil IS NULL AND newer.supersedes IS NOT NULL
            UNWIND newer.supersedes AS olderId
            MATCH (older:Rule {id: olderId})
            WHERE older.validUntil IS NULL AND older.id <> newer.id
            SET older.validUntil = $now, older.invalidatedBy = newer.id
            """,
            new { now },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Superseded rules invalidated (validUntil set as of {Now}) | {Component}", now, "AKG");
    }

    /// <inheritdoc/>
    public async Task ResetAndRebuildEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        await _cypher.ExecuteAsync(
            "MATCH (:Rule)-[:HAS_CHUNK]->(c:RuleChunk) DETACH DELETE c",
            ct: cancellationToken).ConfigureAwait(false);

        await _cypher.ExecuteAsync(
            "MATCH (r:Rule) WHERE r.bodyHash IS NOT NULL OR r.embedding IS NOT NULL OR r.embedAttempts IS NOT NULL "
            + "REMOVE r.bodyHash, r.embedding, r.embedAttempts",
            ct: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("All chunk embeddings cleared; rebuilding from scratch | {Component}", "AKG");
        await _embeddingCache.RebuildAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void CancelEmbeddingRebuild() => _embeddingCache.CancelRebuild();

    private static string BuildGetRulesQuery(string? domain, string? type, string? tag)
    {
        var conditions = new List<string>
        {
            "(r.ownerId IS NULL OR r.ownerId = $userId)",
        };

        if (domain != null) conditions.Add("r.domain = $domain");
        if (type != null) conditions.Add("r.type = $type");
        if (tag != null) conditions.Add("$tag IN r.tags");

        return $"MATCH (r:Rule) WHERE {string.Join(" AND ", conditions)} RETURN r";
    }

    private static int ToInt(object? value) => Convert.ToInt32(value ?? 0);
}
