namespace Edda.Core.Models;

/// <summary>
/// Tuning parameters for <see cref="Edda.Core.Abstractions.IDocumentChunker"/>. Character-based, so no model
/// tokenizer dependency is required; as a rule of thumb roughly 4 characters correspond to 1 token for
/// English/Latin-script text.
/// </summary>
public sealed record ChunkingOptions
{
    /// <summary>Default maximum chunk size in characters.</summary>
    public const int DefaultMaxChars = 1200;

    /// <summary>Default overlap between adjacent text chunks in characters.</summary>
    public const int DefaultOverlapChars = 150;

    /// <summary>
    /// Whether chunking is active. When false, the whole body is emitted as a single chunk — behaviour
    /// identical to the previous one-embedding-per-document model.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Target maximum chunk size in characters. Values below 1 fall back to <see cref="DefaultMaxChars"/>.</summary>
    public int MaxChars { get; init; } = DefaultMaxChars;

    /// <summary>
    /// Overlap between adjacent text chunks in characters, improving recall across chunk boundaries.
    /// Negative values are treated as 0; effective overlap is capped below half of <see cref="MaxChars"/>.
    /// </summary>
    public int OverlapChars { get; init; } = DefaultOverlapChars;
}
