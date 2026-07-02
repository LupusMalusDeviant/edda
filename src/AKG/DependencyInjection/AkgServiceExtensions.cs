using Edda.AKG.Activity;
using Edda.AKG.Benchmark;
using Edda.AKG.Chunking;
using Edda.AKG.Confidence;
using Edda.AKG.Context;
using Edda.AKG.Embeddings;
using Edda.AKG.Feedback;
using Edda.AKG.Graph;
using Edda.AKG.Import;
using Edda.AKG.Providers;
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
            sp.GetRequiredService<ILogger<RuleLoader>>()));

        services.AddSingleton<IWorldKnowledgeSeeder>(sp => new WorldKnowledgeSeeder(
            sp.GetRequiredService<IFileSystem>(),
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<ILogger<WorldKnowledgeSeeder>>()));

        services.AddHostedService<WorldKnowledgeSeedHostedService>();

        services.AddSingleton<IGraphValidator>(sp => new GraphValidator(
            sp.GetRequiredService<ICypherExecutor>(),
            sp.GetRequiredService<ILogger<GraphValidator>>()));

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
            RetrievalOptionsResolver.Resolve(configuration)));

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
            sp.GetRequiredService<ILogger<Neo4jKnowledgeGraph>>()));

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
            int.TryParse(configuration?["EMBEDDING_REBUILD_PARALLELISM"], out var dop) && dop > 0 ? dop : 4));

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

        return services;
    }
}
