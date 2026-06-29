using Edda.AKG.Ingestion.Mapping;
using Edda.AKG.Ingestion.Markdown;
using Edda.Core.Abstractions;
using Edda.Core.Models;

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

    private readonly IReadOnlyList<IIngestionSource> _sources;
    private readonly IIngestionEnricher _enricher;
    private readonly IFileSystem _fileSystem;
    private readonly IKnowledgeGraph _graph;
    private readonly IngestionItemMapper _mapper = new();
    private readonly FrontmatterSerializer _serializer = new();

    /// <summary>Initializes a new instance of the <see cref="IngestionPipeline"/> class.</summary>
    /// <param name="sources">All registered ingestion sources; one is selected per request by kind.</param>
    /// <param name="enricher">Optional enricher (applied only when a request enables it).</param>
    /// <param name="fileSystem">File system used to write the generated Markdown.</param>
    /// <param name="graph">Knowledge graph used to upsert the resulting rules and relations.</param>
    public IngestionPipeline(
        IEnumerable<IIngestionSource> sources,
        IIngestionEnricher enricher,
        IFileSystem fileSystem,
        IKnowledgeGraph graph)
    {
        _sources = sources.ToList();
        _enricher = enricher;
        _fileSystem = fileSystem;
        _graph = graph;
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
        var failed = 0;
        var errors = new List<IngestionError>();

        // Bulk mode: upsert every item without the per-rule synchronous inline embedding, so the import is
        // not serialised behind a (possibly slow) embedding provider. One background rebuild embeds the whole
        // batch when the scope closes.
        using (_graph.BeginBulkIngestion())
        {
            foreach (var raw in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

        return new IngestionResult { Imported = imported, Failed = failed, Errors = errors };
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
