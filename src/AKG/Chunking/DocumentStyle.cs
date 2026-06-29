namespace Edda.AKG.Chunking;

/// <summary>The detected overall style of a document, used to pick a chunking strategy.</summary>
internal enum DocumentStyle
{
    /// <summary>Plain running text without strong structure.</summary>
    Prose,

    /// <summary>Markdown: headings, lists, fenced code and/or tables.</summary>
    Markdown,

    /// <summary>Source code (whole-body code file, no surrounding prose).</summary>
    Code,

    /// <summary>Predominantly tabular content (pipe tables).</summary>
    Table,
}

/// <summary>The kind of a single segmented block within a document.</summary>
internal enum BlockKind
{
    /// <summary>Free text / Markdown prose.</summary>
    Text,

    /// <summary>A fenced or whole-body code block — kept atomic unless it exceeds the chunk size.</summary>
    Code,

    /// <summary>A pipe-table block — kept atomic; split by rows (header repeated) only when oversized.</summary>
    Table,
}

/// <summary>A contiguous, single-kind block of a document produced by <see cref="BlockSegmenter"/>.</summary>
/// <param name="Kind">The block kind.</param>
/// <param name="Text">The block text, including its trailing line endings so concatenation is lossless.</param>
internal readonly record struct Block(BlockKind Kind, string Text);
