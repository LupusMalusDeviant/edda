using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Extracts text entries from an in-memory archive (ZIP) without touching the file system. Used by the
/// knowledge importer to read a <c>.md</c> collection / exported knowledge database from an uploaded ZIP.
/// </summary>
public interface IArchiveExtractor
{
    /// <summary>
    /// Reads all entries whose path ends with <paramref name="extension"/> from the archive bytes and
    /// returns their text content.
    /// </summary>
    /// <param name="archive">The raw archive (ZIP) bytes.</param>
    /// <param name="extension">File extension to include (e.g. <c>.md</c>), case-insensitive.</param>
    /// <returns>The matching entries with their text content.</returns>
    IReadOnlyList<ArchiveTextEntry> ExtractTextEntries(byte[] archive, string extension);
}
