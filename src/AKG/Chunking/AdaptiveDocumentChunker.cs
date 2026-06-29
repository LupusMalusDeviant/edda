using System.Text;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Chunking;

/// <summary>
/// Default <see cref="IDocumentChunker"/>: detects the document style, segments the body into atomic
/// code/table blocks plus text blocks, then packs blocks into chunks within the size budget — recursively
/// splitting oversized text/code blocks (with style-specific separators) and splitting oversized tables by
/// rows with the header repeated. Deterministic and free of I/O. Chunks are a retrieval-layer artifact only;
/// the document itself remains a single graph node (see ADR-0008).
/// </summary>
public sealed class AdaptiveDocumentChunker : IDocumentChunker
{
    private static readonly string[] ProseSeparators = ["\n\n", "\n", ". ", " ", ""];

    private static readonly string[] MarkdownSeparators =
        ["\n## ", "\n### ", "\n#### ", "\n\n", "\n", " ", ""];

    private static readonly string[] CodeSeparators =
        ["\nclass ", "\ndef ", "\nfunc ", "\nfunction ", "\npublic ", "\nprivate ", "\n\n", "\n", " ", ""];

    /// <inheritdoc />
    public IReadOnlyList<DocumentChunk> Chunk(
        string text, ChunkingOptions options, string? fileNameHint = null, string? forcedStyle = null)
    {
        var body = text ?? string.Empty;
        var maxChars = options.MaxChars > 0 ? options.MaxChars : ChunkingOptions.DefaultMaxChars;
        var overlap = Math.Clamp(options.OverlapChars, 0, Math.Max(0, maxChars / 2 - 1));
        var style = DocumentStyleDetector.TryParse(forcedStyle) ?? DocumentStyleDetector.Detect(body, fileNameHint);
        var styleName = StyleName(style);

        // Disabled, blank, or small enough → single chunk (document-level behaviour, as before).
        if (!options.Enabled || body.Trim().Length == 0 || body.Length <= maxChars)
            return [new DocumentChunk { Ordinal = 0, Text = body, Style = styleName }];

        var textSeparators = style == DocumentStyle.Markdown ? MarkdownSeparators : ProseSeparators;
        var blocks = BlockSegmenter.Segment(body, style);
        var pieces = Pack(blocks, maxChars, overlap, textSeparators);

        if (pieces.Count == 0)
            return [new DocumentChunk { Ordinal = 0, Text = body, Style = styleName }];

        var chunks = new List<DocumentChunk>(pieces.Count);
        for (var i = 0; i < pieces.Count; i++)
            chunks.Add(new DocumentChunk { Ordinal = i, Text = pieces[i], Style = styleName });

        return chunks;
    }

    private static List<string> Pack(
        IReadOnlyList<Block> blocks, int maxChars, int overlap, IReadOnlyList<string> textSeparators)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();

        foreach (var block in blocks)
        {
            if (block.Text.Length > maxChars)
            {
                Flush(pieces, current);
                pieces.AddRange(block.Kind switch
                {
                    BlockKind.Table => TableSplitter.Split(block.Text, maxChars),
                    BlockKind.Code => RecursiveTextSplitter.Split(block.Text, maxChars, overlap, CodeSeparators),
                    _ => RecursiveTextSplitter.Split(block.Text, maxChars, overlap, textSeparators),
                });
                continue;
            }

            if (current.Length > 0 && current.Length + block.Text.Length > maxChars)
                Flush(pieces, current);
            current.Append(block.Text);
        }

        Flush(pieces, current);
        return pieces.Where(piece => piece.Trim().Length > 0).ToList();
    }

    private static void Flush(List<string> pieces, StringBuilder current)
    {
        if (current.Length == 0)
            return;
        pieces.Add(current.ToString());
        current.Clear();
    }

    private static string StyleName(DocumentStyle style) => style switch
    {
        DocumentStyle.Markdown => "markdown",
        DocumentStyle.Code => "code",
        DocumentStyle.Table => "table",
        _ => "prose",
    };
}
