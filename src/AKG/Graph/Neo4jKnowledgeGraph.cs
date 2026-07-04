using Edda.AKG.Authorization;
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
    private readonly IBackgroundWorkQueue _backgroundWorkQueue;
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
    /// <param name="backgroundWorkQueue">Queue for supervised post-import embedding rebuilds.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="identity">
    /// C1: ambient identity providing the tenant of the current context (user decision: ambient via
    /// <see cref="IIdentityContext"/>, no per-method tenant parameters). Null falls back to the
    /// default tenant — the single-tenant standalone behavior.
    /// </param>
    /// <param name="authorizer">
    /// C2: central role enforcement for rule mutations. Null falls back to an internal
    /// <see cref="RuleAuthorizer"/> over <paramref name="identity"/> — without an identity that is
    /// the legacy owner/admin check.
    /// </param>
    /// <param name="graphStore">
    /// ADR-0013: pluggable graph read store. Null falls back to an internal
    /// <see cref="CypherGraphStore"/> over <paramref name="cypher"/> and <paramref name="identity"/>
    /// — the Cypher-backed default that works for Neo4j and the in-memory dev executor alike.
    /// </param>
    /// <param name="writeAuthorizer">
    /// ADR-0014: dataset-aware write gate; null falls back to a pass-through over
    /// <paramref name="authorizer"/>, keeping the pre-dataset C2 behaviour.
    /// </param>
    internal Neo4jKnowledgeGraph(
        ICypherExecutor cypher,
        IContextCompiler contextCompiler,
        IRuleLoader ruleLoader,
        IWorldKnowledgeSeeder worldKnowledgeSeeder,
        INeo4jEmbeddingCache embeddingCache,
        IHeadVectorStore headVectorStore,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        IBackgroundWorkQueue backgroundWorkQueue,
        ILogger<Neo4jKnowledgeGraph> logger,
        IIdentityContext? identity = null,
        IRuleAuthorizer? authorizer = null,
        IGraphStore? graphStore = null,
        IDatasetWriteAuthorizer? writeAuthorizer = null)
    {
        _cypher = cypher;
        _contextCompiler = contextCompiler;
        _ruleLoader = ruleLoader;
        _worldKnowledgeSeeder = worldKnowledgeSeeder;
        _embeddingCache = embeddingCache;
        _headVectorStore = headVectorStore;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _backgroundWorkQueue = backgroundWorkQueue;
        _logger = logger;
        _identity = identity;
        _authorizer = authorizer ?? new RuleAuthorizer(identity);
        _graphStore = graphStore ?? new CypherGraphStore(cypher, identity, _timeProvider);
        // ADR-0014 Slice 2b: dataset-aware write gate; the pass-through keeps the pre-dataset C2 behaviour.
        _writeAuthorizer = writeAuthorizer ?? new PassThroughDatasetWriteAuthorizer(_authorizer);
    }

    private readonly IIdentityContext? _identity;
    private readonly IRuleAuthorizer _authorizer;
    private readonly IGraphStore _graphStore;
    private readonly IDatasetWriteAuthorizer _writeAuthorizer;

    /// <summary>C1: the ambient tenant of the current context (read per call, never cached).</summary>
    private string Tenant => _identity?.TenantId ?? Tenants.DefaultTenantId;

    /// <inheritdoc/>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var directory = _fileSystem.GetFullPath(KnowledgeDirectory);
        var count = await _ruleLoader.LoadFromDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("AKG reloaded: {Count} rules imported | {Component}", count, "AKG");
    }

    /// <inheritdoc/>
    public Task<KnowledgeRule?> GetRuleAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default)
        => _graphStore.GetRuleAsync(ruleId, userId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<KnowledgeRule>> GetRulesAsync(
        string? domain = null,
        string? type = null,
        string? tag = null,
        string? userId = null,
        CancellationToken cancellationToken = default)
        => _graphStore.GetRulesAsync(domain, type, tag, userId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<KnowledgeRule>> GetRuleHeadsAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
        => _graphStore.GetRuleHeadsAsync(userId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<KnowledgeRule>> FindNeighborsAsync(
        string ruleId,
        string? userId = null,
        CancellationToken cancellationToken = default)
        => _graphStore.FindNeighborsAsync(ruleId, userId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListOwnersAsync(
        string type,
        CancellationToken cancellationToken = default)
        => _graphStore.ListOwnersAsync(type, cancellationToken);

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
        // ADR-0013: the pure graph write (node + temporal edges) lives in the store; embedding stays here.
        await _graphStore.UpsertRuleGraphAsync(rule, cancellationToken).ConfigureAwait(false);

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

        // Supervised background work: a dedicated hosted consumer drains this and cancels it on host
        // shutdown, instead of an unobserved Task.Run that ignored shutdown and detached its failures.
        _backgroundWorkQueue.Enqueue(async ct =>
        {
            try
            {
                await RebuildEmbeddingsAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Post-import embedding rebuild failed — semantic search may be degraded | {Component}", "AKG");
            }
        }, "post-import embedding rebuild");
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

    /// <inheritdoc/>
    public async Task DeleteRuleAsync(
        string ruleId,
        string userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        var rule = await GetRuleAsync(ruleId, userId, cancellationToken).ConfigureAwait(false);
        if (rule is null) return;

        // C2 + ADR-0014: role matrix widened by dataset grants (an Editor of the rule's dataset may mutate it).
        await _writeAuthorizer.EnsureCanMutateAsync(rule, userId, isAdmin, cancellationToken).ConfigureAwait(false);

        // ADR-0013: the soft-delete write (E10) lives in the store; the authorization gate stays here.
        await _graphStore.DeleteRuleGraphAsync(ruleId, userId, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Soft-deleted rule '{RuleId}' by user '{UserId}' | {Component}", ruleId, userId, "AKG");
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

        // C2 + ADR-0014: role matrix widened by dataset grants (an Editor of the rule's dataset may mutate it).
        await _writeAuthorizer.EnsureCanMutateAsync(root, userId, isAdmin, cancellationToken).ConfigureAwait(false);

        // Most heads nest their descendants by id prefix (repo -> its files). The two ingested branch
        // roots aggregate nodes that don't share their prefix, so they map to the whole branch.
        var prefixes = rootId switch
        {
            "git-knowledge" => new[] { "git:", "git-host:", "git-group:" },
            "uploads" => new[] { "upload:" },
            _ => new[] { rootId + ":" },
        };

        // ADR-0013: the count + hard-delete write lives in the store; authorization and prefix resolution
        // (domain policy about how the graph nests ids) stay here.
        var deleted = await _graphStore.DeleteSubtreeGraphAsync(rootId, prefixes, cancellationToken).ConfigureAwait(false);

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
        // ADR-0013: graph-derived counts come from the store; embedding/head-vector coverage and rebuild
        // progress are composed on top here (the embedding layer moves behind IVectorStore in a later slice).
        var s = await _graphStore.GetRuleStatisticsAsync(cancellationToken).ConfigureAwait(false);

        var coverage = await _embeddingCache.GetCoverageAsync(cancellationToken).ConfigureAwait(false);
        var headCoverage = await _headVectorStore.GetCoverageAsync(cancellationToken).ConfigureAwait(false);

        return new GraphStats
        {
            TotalRules = s.TotalRules,
            GlobalRules = s.GlobalRules,
            UserRules = s.UserRules,
            RulesByDomain = s.RulesByDomain,
            RulesByType = s.RulesByType,
            TotalEdges = s.TotalEdges,
            RulesWithValidators = s.RulesWithValidators,
            RulesWithEmbeddings = s.RulesWithEmbeddings,
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
        // ADR-0013: the supersede-invalidation write (C9) lives in the store.
        await _graphStore.InvalidateSupersededAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Superseded rules invalidated | {Component}", "AKG");
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

}
