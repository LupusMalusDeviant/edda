using System.IO.Compression;
using System.Text;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Import;

/// <summary>
/// <see cref="IArchiveExtractor"/> backed by <see cref="ZipArchive"/> over an in-memory stream — it never
/// writes to disk, so it does not require <c>IFileSystem</c> and is immune to zip-slip (entries are read,
/// never extracted to a path).
/// </summary>
public sealed class ZipArchiveExtractor : IArchiveExtractor
{
    /// <inheritdoc />
    public IReadOnlyList<ArchiveTextEntry> ExtractTextEntries(byte[] archive, string extension)
    {
        var entries = new List<ArchiveTextEntry>();
        using var stream = new MemoryStream(archive);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            // Skip directory entries (empty Name) and files that don't match the requested extension.
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            if (!entry.FullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);
            entries.Add(new ArchiveTextEntry { Path = entry.FullName, Content = reader.ReadToEnd() });
        }

        return entries;
    }
}
