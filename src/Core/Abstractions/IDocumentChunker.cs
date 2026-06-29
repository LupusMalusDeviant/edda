using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Splits a document body into ordered, embeddable chunks, adapting the strategy to the document's style
/// (prose, Markdown, code, tables). Chunking is a retrieval-layer concern only: the resulting chunks are
/// embedded and indexed for semantic search, while the document itself stays the unit represented in the
/// knowledge graph. Implementations are deterministic and perform no I/O.
/// </summary>
public interface IDocumentChunker
{
    /// <summary>Splits <paramref name="text"/> into ordered chunks.</summary>
    /// <param name="text">The document body to split.</param>
    /// <param name="options">Chunk-size and overlap tuning.</param>
    /// <param name="fileNameHint">
    /// Optional file name or path; its extension helps classify code documents (e.g. <c>.py</c>, <c>.cs</c>).
    /// </param>
    /// <param name="forcedStyle">
    /// Optional explicit style (<c>prose</c>/<c>markdown</c>/<c>code</c>/<c>table</c>) that overrides
    /// auto-detection; null/unknown falls back to detection.
    /// </param>
    /// <returns>
    /// The chunks in document order. Returns a single chunk covering the whole body when chunking is
    /// disabled or the body fits within one chunk.
    /// </returns>
    IReadOnlyList<DocumentChunk> Chunk(
        string text, ChunkingOptions options, string? fileNameHint = null, string? forcedStyle = null);
}
