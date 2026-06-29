namespace Edda.Core.Models;

/// <summary>
/// A single chunk produced from a document body by an <see cref="Edda.Core.Abstractions.IDocumentChunker"/>.
/// Chunks are an internal retrieval-layer artifact: they are embedded and indexed for semantic search, but
/// are never surfaced as graph nodes — the document remains the unit shown in the knowledge graph.
/// </summary>
public sealed record DocumentChunk
{
    /// <summary>Zero-based position of this chunk within its document, giving the chunks a stable order.</summary>
    public required int Ordinal { get; init; }

    /// <summary>The chunk text that is embedded.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The document style this chunk was produced under (e.g. <c>prose</c>, <c>markdown</c>, <c>code</c>,
    /// <c>table</c>). Retained for diagnostics; it does not affect retrieval.
    /// </summary>
    public required string Style { get; init; }
}
