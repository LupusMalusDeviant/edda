using System.Text;

namespace Edda.AKG.Chunking;

/// <summary>
/// Splits a document into contiguous, single-kind <see cref="Block"/>s: fenced code blocks and pipe-table
/// blocks are isolated as atomic units so they are never cut mid-structure, everything else is grouped as
/// text. A whole-body code document (style <see cref="DocumentStyle.Code"/>) becomes a single code block.
/// Block texts retain their original line endings so concatenating all blocks reproduces the input.
/// </summary>
internal static class BlockSegmenter
{
    /// <summary>Segments <paramref name="text"/> into typed blocks.</summary>
    /// <param name="text">The document body.</param>
    /// <param name="style">The detected document style.</param>
    /// <returns>The blocks in document order.</returns>
    public static List<Block> Segment(string text, DocumentStyle style)
    {
        if (style == DocumentStyle.Code)
            return text.Length == 0 ? [] : [new Block(BlockKind.Code, text)];

        var blocks = new List<Block>();
        var lines = LineUtilities.SplitKeepEndings(text);
        var buffer = new StringBuilder();

        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (IsFence(line))
            {
                FlushText(blocks, buffer);
                var code = new StringBuilder();
                code.Append(line);
                i++;
                while (i < lines.Count)
                {
                    code.Append(lines[i]);
                    var closing = IsFence(lines[i]);
                    i++;
                    if (closing)
                        break;
                }

                blocks.Add(new Block(BlockKind.Code, code.ToString()));
            }
            else if (IsTableRow(line))
            {
                FlushText(blocks, buffer);
                var table = new StringBuilder();
                while (i < lines.Count && IsTableRow(lines[i]))
                {
                    table.Append(lines[i]);
                    i++;
                }

                blocks.Add(new Block(BlockKind.Table, table.ToString()));
            }
            else
            {
                buffer.Append(line);
                i++;
            }
        }

        FlushText(blocks, buffer);
        return blocks;
    }

    private static void FlushText(List<Block> blocks, StringBuilder buffer)
    {
        if (buffer.Length == 0)
            return;
        blocks.Add(new Block(BlockKind.Text, buffer.ToString()));
        buffer.Clear();
    }

    private static bool IsFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    private static bool IsTableRow(string line) => line.TrimStart().StartsWith('|');
}
