using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates a full ingestion run: select the source, fetch items, map them to knowledge rules,
/// optionally enrich them (see <see cref="IIngestionEnricher"/>), and persist them both as Markdown
/// files and as graph nodes with relations. The pipeline is best-effort and never throws — per-item
/// failures are collected into the returned <see cref="IngestionResult"/>.
/// </summary>
public interface IIngestionPipeline
{
    /// <summary>
    /// Runs the ingestion described by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">The ingestion request (source kind, source config, type mapping, enrichment flag).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of imported/updated/skipped/failed items and any collected errors.</returns>
    Task<IngestionResult> IngestAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default);
}
