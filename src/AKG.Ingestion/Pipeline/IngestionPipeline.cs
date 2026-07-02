using Edda.AKG.Ingestion.Mapping;
using Edda.AKG.Ingestion.Markdown;
using Edda.AKG.Ingestion.Sync;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Edda.AKG.Ingestion.Pipeline;

/// <summary>
/// Default <see cref="IIngestionPipeline"/>: selects a source by kind, fetches its items, maps them to
/// knowledge rules (resolving relations across the full id set), optionally enriches them, then writes
/// each rule as a Markdown file and upserts it into the knowledge graph. Best-effort — per-item failures
/// are collected into the result and never thrown.
/// </summary>
public sealed class IngestionPipeline : IIngestionPipeline
{
    /// <summary>Default root directory for generated Markdown when the request does not specify one.</summary>
    public const string DefaultTargetDirectory = "knowledge/ingested";

    /// <summary>
    /// Environment/config key that opts the auto entity-extraction stage in (separate from the enricher).
    /// When set to <c>true</c>, each ingested item's raw body is run through the entity extractor and the
    /// resulting entities/relations are persisted into the LightRAG-style entity layer (M2 / ADR-0010).
    /// </summary>
    private const string EntityExtractionEnvKey = "INGESTION_ENTITY_EXTRACTION";

    private readonly IReadOnlyList<IIngestionSource> _sources;
    private readonly IIngestionEnricher _enricher;
    private readonly IFileSystem _fileSystem;
    private readonly IKnowledgeGraph _graph;
    private readonly IEntityIngestionService _entityIngestion;
    private readonly IIdentityContext _identity;
    private readonly IConfiguration _configuration;
    private readonly ISyncStateStore? _syncState;
    private readonly IngestionItemMapper _mapper = new();
    private readonly FrontmatterSerializer _serializer = new();

    /// <summary>Initializes a new instance of the <see cref="IngestionPipeline"/> class.</summary>
    /// <param name="sources">All registered ingestion sources; one is selected per request by kind.</param>
    /// <param name="enricher">Optional enricher (applied only when a request enables it).</param>
    /// <param name="fileSystem">File system used to write the generated Markdown.</param>
    /// <param name="graph">Knowledge graph used to upsert the resulting rules and relations.</param>
    /// <param name="entityIngestion">Service that extracts and persists entities per item when opted in.</param>
    /// <param name="identity">Identity context that owns the entities extracted during a run (Regel 6).</param>
    /// <param name="configuration">Configuration source for the entity-extraction opt-in toggle.</param>
    /// <param name="syncState">
    /// Optional incremental-sync store (C5). When provided, items whose source content is unchanged since the
    /// last run are skipped. Null (the default) disables incremental sync — every run is a full ingest, exactly
    /// as before C5.
    /// </param>
    public IngestionPipeline(
        IEnumerable<IIngestionSource> sources,
        IIngestionEnricher enricher,
        IFileSystem fileSystem,
        IKnowledgeGraph graph,
        IEntityIngestionService entityIngestion,
        IIdentityContext identity,
        IConfiguration configuration,
        ISyncStateStore? syncState = null)
    {
        _sources = sources.ToList();
        _enricher = enricher;
        _fileSystem = fileSystem;
        _graph = graph;
        _entityIngestion = entityIngestion;
        _identity = identity;
        _configuration = configuration;
        _syncState = syncState;
    }

    /// <inheritdoc />
    public async Task<IngestionResult> IngestAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s =>
            string.Equals(s.SourceKind, request.SourceKind, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return new IngestionResult
            {
                Failed = 1,
                Errors = [new IngestionError { Message = $"No ingestion source registered for kind '{request.SourceKind}'." }],
            };
        }

        List<IngestionItem> items;
        try
        {
            items = await CollectAsync(source, request.Source, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new IngestionResult
            {
                Failed = 1,
                Errors = [new IngestionError { Message = $"Source '{request.SourceKind}' failed: {ex.Message}" }],
            };
        }

        // Sources may legitimately emit the same structural node more than once (e.g. a group source
        // yielding the shared git-knowledge root once per repository); process each id only once.
        items = items
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var knownIds = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var targetRoot = string.IsNullOrWhiteSpace(request.TargetDirectory)
            ? DefaultTargetDirectory
            : request.TargetDirectory;

        var imported = 0;
        var skipped = 0;
        var failed = 0;
        var errors = new List<IngestionError>();

        // C5: incremental sync — load the prior per-instance content-hash manifest and skip unchanged items.
        // Disabled (full ingest) when no store is configured or ForceFullSync is set.
        var instanceKey = IngestionInstanceKey.For(request);
        var incremental = _syncState is not null && !request.ForceFullSync;
        var priorHashes = incremental
            ? (await _syncState!.LoadAsync(instanceKey, cancellationToken).ConfigureAwait(false)).ItemHashes
            : (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal);
        var newHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        // Auto entity-extraction is opt-in and separate from the enricher: resolve the toggle and the owning
        // user once per run (Regel 6 — the owner is the ingesting identity, never a request field).
        var extractEntities = string.Equals(
            _configuration[EntityExtractionEnvKey], "true", StringComparison.OrdinalIgnoreCase);
        var entityOwner = _identity.UserId ?? "local";

        // Bulk mode: upsert every item without the per-rule synchronous inline embedding, so the import is
        // not serialised behind a (possibly slow) embedding provider. One background rebuild embeds the whole
        // batch when the scope closes.
        using (_graph.BeginBulkIngestion())
        {
            foreach (var raw in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // C5: skip items whose source content is unchanged since the last run (no upsert, no re-embed).
                var contentHash = IngestionContentHash.Compute(raw);
                if (incremental
                    && priorHashes.TryGetValue(raw.Id, out var previous)
                    && string.Equals(previous, contentHash, StringComparison.Ordinal))
                {
                    skipped++;
                    newHashes[raw.Id] = contentHash;   // carry forward so the next run still compares correctly
                    continue;
                }

                try
                {
                    var item = request.EnableEnrichment
                        ? await _enricher.EnrichAsync(raw, knownIds, cancellationToken).ConfigureAwait(false)
                        : raw;

                    var rule = _mapper.Map(item, request.TypeMapping, knownIds);
                    var markdown = _serializer.Serialize(rule, item.Title);

                    var directory = _fileSystem.CombinePath(targetRoot, rule.Domain);
                    _fileSystem.EnsureDirectoryExists(directory);
                    await _fileSystem.WriteAllTextAsync(
                        _fileSystem.CombinePath(directory, ToFileName(rule.Id)),
                        markdown,
                        cancellationToken).ConfigureAwait(false);

                    await _graph.UpsertRuleAsync(rule, cancellationToken).ConfigureAwait(false);
                    imported++;

                    // Opt-in, separate from the enricher (M2 / ADR-0010): extract entities from the raw body
                    // and persist them into the user-scoped entity layer. Best-effort — the service never
                    // throws, so entity extraction never fails a document import.
                    if (extractEntities)
                    {
                        await _entityIngestion
                            .IngestTextAsync(raw.Body, rule.Domain, entityOwner, request.SourceKind, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // C5: record the hash only on success → a failed item is retried on the next run.
                    newHashes[raw.Id] = contentHash;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add(new IngestionError { ItemId = raw.Id, Message = ex.Message });
                }
            }
        }

        // C5: persist the new manifest best-effort — a save failure only forfeits the next run's skip
        // optimization, it never corrupts the graph (the rules are already upserted).
        if (_syncState is not null)
        {
            try
            {
                await _syncState
                    .SaveAsync(instanceKey, new IngestionSyncState { ItemHashes = newHashes }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested is false)
            {
                // Best-effort — swallow; the next run simply falls back to a full ingest.
            }
        }

        return new IngestionResult { Imported = imported, Skipped = skipped, Failed = failed, Errors = errors };
    }

    private static async Task<List<IngestionItem>> CollectAsync(
        IIngestionSource source,
        IngestionSourceConfig config,
        CancellationToken cancellationToken)
    {
        var items = new List<IngestionItem>();
        await foreach (var item in source.FetchAsync(config, cancellationToken).ConfigureAwait(false))
            items.Add(item);
        return items;
    }

    /// <summary>Turns a (possibly separator-laden) rule id into a filesystem-safe Markdown file name.</summary>
    internal static string ToFileName(string id)
    {
        var chars = id
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray();
        return new string(chars) + ".md";
    }
}
