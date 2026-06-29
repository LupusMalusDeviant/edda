namespace Edda.Core.Models;

/// <summary>A single text entry extracted from an archive (e.g. a <c>.md</c> file inside a ZIP).</summary>
public sealed record ArchiveTextEntry
{
    /// <summary>The entry's path within the archive.</summary>
    public required string Path { get; init; }

    /// <summary>The decoded UTF-8 text content of the entry.</summary>
    public required string Content { get; init; }
}
