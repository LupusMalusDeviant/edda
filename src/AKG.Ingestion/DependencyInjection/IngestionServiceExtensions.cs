using Edda.AKG.Ingestion.Connectors;
using Edda.AKG.Ingestion.Enrichment;
using Edda.AKG.Ingestion.Entities;
using Edda.AKG.Ingestion.Git;
using Edda.AKG.Ingestion.Import;
using Edda.AKG.Ingestion.GitLab;
using Edda.AKG.Ingestion.Llm;
using Edda.AKG.Ingestion.Pipeline;
using Edda.AKG.Ingestion.Sources;
using Edda.AKG.Ingestion.Sync;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Edda.AKG.Ingestion.DependencyInjection;

/// <summary>
/// Dependency-injection registration for the knowledge-ingestion pipeline: the Git source, the
/// LibGit2Sharp-based clone client (ADR-0002), the default no-op enricher, and the pipeline itself.
/// </summary>
public static class IngestionServiceExtensions
{
    private const string CacheRootKey = "INGEST_GIT_CACHE";
    private const string UsernameKey = "INGEST_GIT_USERNAME";
    private const string TokenKey = "INGEST_GIT_TOKEN";
    private const string ImportAllowValidatorsKey = "IMPORT_ALLOW_VALIDATORS";
    private const string DefaultCacheRoot = "data/ingest-cache";

    /// <summary>
    /// Registers the ingestion sources, enricher and pipeline. Configuration keys: <c>INGEST_GIT_CACHE</c>
    /// (clone cache root), <c>INGEST_GIT_USERNAME</c> and <c>INGEST_GIT_TOKEN</c> (credentials for
    /// private repositories). The default enricher is the no-op variant, keeping ingestion local-only
    /// unless an LLM enricher is registered explicitly (see ADR-0001).
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">Configuration source for ingestion settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIngestionServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var cacheRoot = configuration[CacheRootKey];
        if (string.IsNullOrWhiteSpace(cacheRoot))
            cacheRoot = DefaultCacheRoot;
        var username = configuration[UsernameKey];
        var token = configuration[TokenKey];

        services.AddSingleton<IGitClient>(_ => new LibGit2SharpGitClient(cacheRoot, username, token));
        services.AddSingleton<GitMarkdownSource>();
        services.AddSingleton<IIngestionSource>(sp => sp.GetRequiredService<GitMarkdownSource>());

        // C5: per-instance incremental-sync manifest under data/sync-state (skips unchanged items on re-ingest).
        // MS-DI fills the pipeline's optional ISyncStateStore parameter from this registration.
        services.AddSingleton<ISyncStateStore>(sp => new FileSyncStateStore(sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<IIngestionPipeline, IngestionPipeline>();
        AddEnricher(services);
        AddEntityExtraction(services);

        // GitLab group batch source: base URL + token are supplied per source instance by the connector,
        // so the client is built per run via the factory (see ADR-0006) rather than from a fixed singleton.
        services.AddSingleton<IGitLabClientFactory, HttpGitLabClientFactory>();
        services.AddSingleton<IIngestionSource, GitLabGroupSource>();

        // Generic HTTP/REST source (custom-http): any JSON API mapped to items via descriptor config (ADR-0006).
        services.AddSingleton<HttpApiSource>();
        services.AddSingleton<IIngestionSource>(sp => sp.GetRequiredService<HttpApiSource>());

        // Config-driven knowledge connectors (descriptor-rendered in the UI; new types just add a connector).
        services.AddSingleton<IKnowledgeConnector, GitKnowledgeConnector>();
        services.AddSingleton<IKnowledgeConnector, GitLabGroupKnowledgeConnector>();
        services.AddSingleton<IKnowledgeConnector, CustomHttpKnowledgeConnector>();
        services.AddSingleton<IKnowledgeConnector, JiraKnowledgeConnector>();
        services.AddSingleton<IKnowledgeConnector, AworkKnowledgeConnector>();
        services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();

        // Upload import (.md / .zip / .pdf): mapped through the same item builder + mapper as Git ingestion,
        // so uploads form a connected hierarchy instead of flat nodes (extractors are registered by AKG).
        // Foreign bundles have their validatorScript stripped by default (F17); IMPORT_ALLOW_VALIDATORS=true opts in.
        var allowImportedValidators = bool.TryParse(configuration[ImportAllowValidatorsKey], out var allow) && allow;
        services.AddSingleton<IKnowledgeImporter>(sp => new KnowledgeImporter(
            sp.GetRequiredService<IKnowledgeGraph>(),
            sp.GetRequiredService<IArchiveExtractor>(),
            sp.GetRequiredService<IPdfTextExtractor>(),
            allowImportedValidators));

        // F16: opt-in LLM judge for TDK rules (validatorType: llm). Default off — registered only when
        // TDK_LLM_JUDGE=true; degrades to an engine error when no LLM provider is configured. The TDK
        // engine takes the judge as an optional dependency, so absence simply skips llm rules.
        if (string.Equals(configuration["TDK_LLM_JUDGE"], "true", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ITdkLlmJudge, LlmTdkJudge>();

        return services;
    }

    /// <summary>
    /// Registers the live enrichment stack: the chat-client factory, a resolving chat client (provider and
    /// key resolved at call time from settings + credential store, with <c>INGESTION_LLM_*</c> env fallback),
    /// the LLM enricher, and a resolving enricher that applies it only when enrichment is enabled (settings
    /// <c>Enabled</c>, falling back to <c>INGESTION_ENRICHER=llm</c>). All toggles take effect without a
    /// restart (see ADR-0001 and ADR-0004) — content leaves the system only when enrichment is enabled.
    /// </summary>
    private static void AddEnricher(IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<ILlmChatClientFactory, LlmChatClientFactory>();
        services.AddSingleton<ILlmChatClient, ResolvingLlmChatClient>();
        services.AddSingleton<LlmIngestionEnricher>();
        services.AddSingleton<IIngestionEnricher, ResolvingIngestionEnricher>();
    }

    /// <summary>
    /// Registers the LLM-backed entity-extraction stack (M2 / ADR-0010): the entity extractor (reusing the
    /// resolving chat client) and the ingestion service that persists extracted entities/relations into the
    /// LightRAG-style entity layer. Extraction runs only when explicitly invoked (opt-in endpoint or
    /// pipeline) and is best-effort — a missing or failing LLM leaves the graph unchanged.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    private static void AddEntityExtraction(IServiceCollection services)
    {
        services.AddSingleton<IEntityExtractor, LlmEntityExtractor>();
        services.AddSingleton<IEntityIngestionService, EntityIngestionService>();
    }
}
