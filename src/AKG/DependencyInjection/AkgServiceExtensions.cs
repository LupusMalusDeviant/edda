using Edda.AKG.Activity;
using Edda.AKG.Background;
using Edda.AKG.Benchmark;
using Edda.AKG.Chunking;
using Edda.AKG.Confidence;
using Edda.AKG.Context;
using Edda.AKG.Embeddings;
using Edda.AKG.Feedback;
using Edda.AKG.Graph;
using Edda.AKG.Import;
using Edda.AKG.Providers;
using Edda.AKG.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.DependencyInjection;

/// <summary>
/// Extension methods for registering AKG services with the DI container.
/// Called from Gateway/Program.cs as part of the composition root.
/// </summary>
public static class AkgServiceExtensions
{
    /// <summary>
    /// Registers all AKG services: graph database provider, ICypherExecutor, IKnowledgeGraph,
    /// IDomainManager, ContextCompiler, RuleLoader, Neo4jEmbeddingCache,
    /// and RuleConfidenceStore.
    /// The graph provider is selected via <c>GRAPH_PROVIDER</c> env-var (default: neo4j).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// Optional configuration for graph database connection settings.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAkgServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Graph provider configuration
        var providerName = configuration?["GRAPH_PROVIDER"]
            ?? Environment.GetEnvironmentVariable("GRAPH_PROVIDER")
            ?? "neo4j";

        var uri = configuration?["Neo4j:Uri"]
            ?? Environment.GetEnvironmentVariable("NEO4J_URI")
            ?? "bolt://localhost:7687";
        var username = configuration?["Neo4j:Username"]
            ?? Environment.GetEnvironmentVariable("NEO4J_USERNAME")
            ?? "neo4j";
        var password = configuration?["Neo4j:Password"]
            ?? Environment.GetEnvironmentVariable("NEO4J_PASSWORD")
            ?? "password";

        // Memgraph defaults to no authentication
        var defaultAuth = string.Equals(providerName, "memgraph", StringComparison.OrdinalIgnoreCase)
            ? "none" : "basic";
        var auth = configuration?["Neo4j:Auth"]
            ?? Environment.GetEnvironmentVariable("NEO4J_AUTH_MODE")
            ?? defaultAuth;

        var config = new GraphProviderConfig
        {
            Provider = providerName,
            Uri = uri,
            Username = username,
            Password = password,
            Auth = auth,
        };

        // Graph database provider — selected by GRAPH_PROVIDER env-var
        services.AddSingleton<IGraphDatabaseProvider>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return providerName.ToLowerInvariant() switch
            {
                "memory" => new MemoryGraphDatabaseProvider(loggerFactory),
                "memgraph" => new MemgraphGraphDatabaseProvider(config, loggerFactory),
                _ => new Neo4jGraphDatabaseProvider(config, loggerFactory),
            };
        });

        // ICypherExecutor — created by the graph provider
        services.AddSingleton<ICypherExecutor>(sp =>
            sp.GetRequiredService<IGraphDatabaseProvider>().CreateExecutor());

        // Graph helpers
        services.AddSingleton<IRuleLoader>(sp => new RuleLoader(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RuleLoader>>()));

        services.AddSingleton<IWorldKnowledgeSeeder>(sp => new WorldKnowledgeSeeder(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<ILogger<WorldKnowledgeSeeder>>()));

        services.AddHostedService<WorldKnowledgeSeedHostedService>();

        // D2 — supervised background work queue: a Channel-backed queue drained by a single hosted
        // consumer, replacing unobserved Task.Run calls so background jobs respect shutdown and surface
        // their failures. Consumed by Neo4jKnowledgeGraph (post-import rebuild), the embed-rebuild
        // endpoint, and WorldKnowledgeSeedHostedService (superseded-rule invalidation).
        services.AddSingleton<IBackgroundWorkQueue, ChannelBackgroundWorkQueue>();
        services.AddHostedService<BackgroundWorkQueueConsumer>();

        services.AddSingleton<IGraphValidator>(sp => new GraphValidator(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<ILogger<GraphValidator>>()));

        // C2 — central role enforcement (ADR-0012): ONE authorizer over the ambient identity replaces
        // the scattered owner/admin checks. Without a registered identity it keeps the legacy semantics.
        services.AddSingleton<IRuleAuthorizer>(sp =>
            new Authorization.RuleAuthorizer(sp.GetService<IIdentityContext>()));

        // Context compiler (orchestrates all 4 phases + F32 feedback multiplier)
        services.AddSingleton<IContextCompiler>(sp => new ContextCompiler(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<ILogger<ContextCompiler>>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IRuleFeedbackService>(),
            sp.GetService<IEntityStore>(),
            sp.GetRequiredService<IHeadVectorStore>(),
            RetrievalOptionsResolver.Resolve(configuration),
            // C1: ambient tenant source (user decision) — null falls back to the default tenant.
            sp.GetService<IIdentityContext>()));

        // Knowledge graph (main public API)
        services.AddSingleton<IKnowledgeGraph>(sp => new Neo4jKnowledgeGraph(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<IContextCompiler>(),
            sp.GetRequiredService<IRuleLoader>(),
            sp.GetRequiredService<IWorldKnowledgeSeeder>(),
            sp.GetRequiredService<INeo4jEmbeddingCache>(),
            sp.GetRequiredService<IHeadVectorStore>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IBackgroundWorkQueue>(),
            sp.GetRequiredService<ILogger<Neo4jKnowledgeGraph>>(),
            // C1: ambient tenant source (user decision) — null falls back to the default tenant.
            sp.GetService<IIdentityContext>(),
            // C2: central role matrix for delete/subtree mutations.
            sp.GetRequiredService<IRuleAuthorizer>()));

        // F48 — retrieval benchmark runner (measures CompileContextAsync quality)
        services.AddSingleton<IBenchmarkRunner>(sp => new AkgBenchmarkRunner(
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<AkgBenchmarkRunner>>()));

        // F49 — LightRAG-style entity layer store
        services.AddSingleton<IEntityStore>(sp => new Neo4jEntityStore(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<Neo4jEntityStore>>()));

        // Domain manager
        services.AddSingleton<IDomainManager>(sp => new DomainManager(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<ILogger<DomainManager>>()));

        // Adaptive document chunker (deterministic, stateless)
        services.AddSingleton<IDocumentChunker, AdaptiveDocumentChunker>();

        // Cross-cutting activity tracker for the global progress indicator (import/chunking/embedding)
        services.AddSingleton<IActivityTracker, ActivityTracker>();

        // Embedding cache (chunks each rule body, embeds and stores hidden :RuleChunk children)
        services.AddSingleton<INeo4jEmbeddingCache>(sp => new Neo4jEmbeddingCache(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<IDocumentChunker>(),
            () => ChunkingOptionsResolver.Resolve(
                sp.GetRequiredService<ISettingsService>().Current.Chunking, configuration),
            sp.GetRequiredService<ILogger<Neo4jEmbeddingCache>>(),
            sp.GetRequiredService<IActivityTracker>(),
            int.TryParse(configuration?["EMBEDDING_REBUILD_PARALLELISM"], out var dop) && dop > 0 ? dop : 4,
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            // B2: fingerprint = active provider:model:dimension. Stored on chunks so a provider/model/dimension
            // change re-embeds the affected chunks (and a dimension change recreates the vector index).
            () =>
            {
                var embedding = sp.GetRequiredService<ISettingsService>().Current.Embedding;
                var service = sp.GetRequiredService<IEmbeddingService>();
                return $"{embedding.Provider ?? "null"}:{embedding.Model ?? "default"}:{service.Dimensions}";
            }));

        // Head-vector store (ADR-0009) — per-repo centroids for hierarchical stage-1 pre-pruning.
        services.AddSingleton<IHeadVectorStore>(sp => new Neo4jHeadVectorStore(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<ILogger<Neo4jHeadVectorStore>>()));

        // Background embedding backfill — keeps the cache complete, resilient + resumable across restarts.
        var backfillInterval = int.TryParse(
            configuration?["EMBEDDING_BACKFILL_INTERVAL_SECONDS"]
            ?? Environment.GetEnvironmentVariable("EMBEDDING_BACKFILL_INTERVAL_SECONDS"),
            out var bi) && bi > 0 ? bi : 60;

        services.AddHostedService(sp => new EmbeddingBackfillHostedService(
            sp.GetRequiredService<INeo4jEmbeddingCache>(),
            sp.GetRequiredService<IHeadVectorStore>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<ILogger<EmbeddingBackfillHostedService>>(),
            backfillInterval));

        // Confidence store (in-memory, thread-safe)
        services.AddSingleton<RuleConfidenceStore>();

        // IRuleConfidenceStore: sliding-window implementation for TDK engine
        services.AddSingleton<IRuleConfidenceStore, SlidingWindowRuleConfidenceStore>();

        // E8 — batch tag/priority operations over a set of rules (used by the UI + POST /api/akg/rules/batch).
        // C2: per-rule mutations go through the central role matrix.
        services.AddSingleton<IRuleBatchService>(sp => new Rules.RuleBatchService(
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<ILogger<Rules.RuleBatchService>>(),
            sp.GetRequiredService<IRuleAuthorizer>()));

        // F32 — Rule Feedback Loop (SQLite-backed, optional — disabled if path not set)
        var feedbackDbPath = configuration?["Feedback:DbPath"]
            ?? Environment.GetEnvironmentVariable("FEEDBACK_DB_PATH")
            ?? "data/feedback.db";

        services.AddSingleton<IRuleFeedbackStore>(sp => new RuleFeedbackStore(
            feedbackDbPath,
            sp.GetRequiredService<ILogger<RuleFeedbackStore>>()));

        // F32b — confidence decay: stale feedback reverts a rule's multiplier toward neutral over time.
        var decayRaw = configuration?["Feedback:DecayHalfLifeDays"]
            ?? Environment.GetEnvironmentVariable("FEEDBACK_DECAY_HALFLIFE_DAYS");
        var decayHalfLifeDays = double.TryParse(
            decayRaw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsedHalfLife)
            ? parsedHalfLife
            : ConfidenceAdjuster.DefaultDecayHalfLifeDays;

        services.AddSingleton<IRuleFeedbackService>(sp => new RuleFeedbackService(
            sp.GetRequiredService<IRuleFeedbackStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RuleFeedbackService>>(),
            decayHalfLifeDays));

        services.AddHostedService<FeedbackSummaryJob>();

        // Import extractors (in-memory). The importer that consumes them lives in AKG.Ingestion so it can
        // reuse the ingestion item builder + mapper, yielding a connected hierarchy instead of flat nodes.
        services.AddSingleton<IArchiveExtractor, ZipArchiveExtractor>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();

        // F5: TDK validator self-test. The verifier parses the knowledge directory and runs each rule's
        // validator against its validatorFixtures via the sandbox (with the F4 helper). ISandboxFactory
        // and ITdkHelperModule are registered by the host (Sandboxing / AddTdkEngine); resolved lazily.
        services.AddSingleton<ITdkFixtureVerifier>(sp => new TdkFixtureVerifier(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ISandboxFactory>(),
            sp.GetRequiredService<ITdkHelperModule>(),
            sp.GetRequiredService<ILogger<TdkFixtureVerifier>>(),
            knowledgeDirectory: "knowledge"));

        // Registered after WorldKnowledgeSeedHostedService so the knowledge/ files exist when it runs.
        // Default off (TDK_FIXTURE_SELFTEST); self-gating so registration is unconditional.
        services.AddHostedService(sp => new TdkFixtureSelfTestHostedService(
            sp.GetRequiredService<ITdkFixtureVerifier>(),
            sp.GetRequiredService<ILogger<TdkFixtureSelfTestHostedService>>(),
            enabled: ParseFlag(configuration, "TDK_FIXTURE_SELFTEST"),
            strict: ParseFlag(configuration, "TDK_FIXTURE_SELFTEST_STRICT")));

        // E10: recycle bin over soft-deleted rules (list/restore/purge). IAuditLog is registered by
        // the host (Security layer); resolved lazily.
        services.AddSingleton<IRuleRecycleBin>(sp => new RuleRecycleBin(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<IAuditLog>(),
            sp.GetRequiredService<ILogger<RuleRecycleBin>>(),
            sp.GetService<IIdentityContext>(),
            // C2: central role matrix for restore/purge.
            sp.GetRequiredService<IRuleAuthorizer>()));

        return services;
    }

    /// <summary>
    /// Reads a boolean flag from configuration (falling back to the environment). Returns
    /// <see langword="true"/> only when the value equals <c>"true"</c> (case-insensitive); unset or
    /// any other value is <see langword="false"/> — preserving the default-off behavior.
    /// </summary>
    /// <param name="configuration">The host configuration, if available.</param>
    /// <param name="key">The configuration/environment key to read.</param>
    /// <returns>The parsed flag.</returns>
    private static bool ParseFlag(IConfiguration? configuration, string key)
    {
        var raw = configuration?[key] ?? Environment.GetEnvironmentVariable(key);
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }
}
