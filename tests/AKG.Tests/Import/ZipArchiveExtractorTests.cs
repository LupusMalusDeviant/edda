using System.IO.Compression;
using System.Text;
using Edda.AKG.Import;

namespace Edda.AKG.Tests.Import;

/// <summary>Unit tests for the in-memory <see cref="ZipArchiveExtractor"/>.</summary>
public sealed class ZipArchiveExtractorTests
{
    private static byte[] BuildZip(params (string Path, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }

    [Fact]
    public void ExtractTextEntries_ReturnsOnlyMatchingExtension_WithContent()
    {
        var zip = BuildZip(("docs/a.md", "# A"), ("b.md", "# B"), ("notes.txt", "ignore"));

        var entries = new ZipArchiveExtractor().ExtractTextEntries(zip, ".md");

        entries.Should().HaveCount(2);
        entries.Select(e => e.Path).Should().BeEquivalentTo(["docs/a.md", "b.md"]);
        entries.Single(e => e.Path == "docs/a.md").Content.Should().Be("# A");
    }

    [Fact]
    public void ExtractTextEntries_NoMatches_ReturnsEmpty()
    {
        var zip = BuildZip(("readme.txt", "x"));

        new ZipArchiveExtractor().ExtractTextEntries(zip, ".md").Should().BeEmpty();
    }
}
