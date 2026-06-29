using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Imports knowledge from uploaded files into the graph: a single Markdown file, a ZIP collection of
/// Markdown files (e.g. another project's exported knowledge database), or a PDF. Best-effort: per-item
/// failures are reported in the returned <see cref="IngestionResult"/> rather than thrown.
/// </summary>
public interface IKnowledgeImporter
{
    /// <summary>
    /// Imports the given uploaded file, dispatching by extension (<c>.md</c>/<c>.markdown</c>, <c>.zip</c>,
    /// <c>.pdf</c>).
    /// </summary>
    /// <param name="fileName">The original file name (used to detect the type and to derive ids/titles).</param>
    /// <param name="content">The raw file bytes.</param>
    /// <param name="domain">Optional domain to assign to imported rules; null uses a default.</param>
    /// <param name="chunkStyle">
    /// Optional forced chunking style for the uploaded documents (<c>prose</c>/<c>markdown</c>/<c>code</c>/
    /// <c>table</c>); null/unknown lets the chunker auto-detect per document.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Counts of imported/failed items and any collected errors.</returns>
    Task<IngestionResult> ImportAsync(
        string fileName,
        byte[] content,
        string? domain,
        string? chunkStyle = null,
        CancellationToken cancellationToken = default);
}
