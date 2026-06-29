using System.Text;
using System.Text.Json;
using Edda.AKG.Ingestion.Import;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Import;

/// <summary>
/// Unit tests for <see cref="KnowledgeImporter"/>: uploads must map to a connected hierarchy
/// (root → source node → files) with resolved links — not flat, isolated nodes.
/// </summary>
public sealed class KnowledgeImporterTests
{
    private static (KnowledgeImporter Sut, List<KnowledgeRule> Upserted) CreateSut(
        IReadOnlyList<ArchiveTextEntry>? archiveEntries = null,
        string? pdfText = null)
    {
        var upserted = new List<KnowledgeRule>();
        var graph = new Mock<IKnowledgeGraph>();
        graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .Callback<KnowledgeRule, CancellationToken>((r, _) => upserted.Add(r))
            .ReturnsAsync<KnowledgeRule, CancellationToken, IKnowledgeGraph, KnowledgeRule>((r, _) => r);

        var archive = new Mock<IArchiveExtractor>();
        archive.Setup(a => a.ExtractTextEntries(It.IsAny<byte[]>(), It.IsAny<string>()))
            .Returns(archiveEntries ?? []);

        var pdf = new Mock<IPdfTextExtractor>();
        pdf.Setup(p => p.Extract(It.IsAny<byte[]>())).Returns(pdfText ?? string.Empty);

        return (new KnowledgeImporter(graph.Object, archive.Object, pdf.Object), upserted);
    }

    private static byte[] Bytes(string text = "x") => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task ImportAsync_Markdown_BuildsConnectedHierarchy()
    {
        var (sut, upserted) = CreateSut();

        var result = await sut.ImportAsync("guide.md", Bytes("# Guide\n\nText."), domain: "docs");

        result.Failed.Should().Be(0);
        upserted.Select(r => r.Id).Should().Contain(new[] { "uploads", "upload:docs", "upload:docs:guide" });
        upserted.Single(r => r.Id == "upload:docs:guide").RelatesTo!.Related.Should().Contain("upload:docs");
    }

    [Fact]
    public async Task ImportAsync_Zip_MapsEntries_AndResolvesLinks()
    {
        var entries = new[]
        {
            new ArchiveTextEntry { Path = "docs/a.md", Content = "# A\n\n[B](b.md)" },
            new ArchiveTextEntry { Path = "docs/b.md", Content = "# B" },
        };
        var (sut, upserted) = CreateSut(archiveEntries: entries);

        var result = await sut.ImportAsync("repo.zip", Bytes(), domain: null);

        result.Failed.Should().Be(0);
        upserted.Select(r => r.Id).Should().Contain(new[] { "upload:repo:docs/a", "upload:repo:docs/b", "upload:repo" });
        upserted.Single(r => r.Id == "upload:repo:docs/a").RelatesTo!.Related.Should().Contain("upload:repo:docs/b");
        upserted.Single(r => r.Id == "upload:repo:docs/a").Domain.Should().Be("docs");
    }

    [Fact]
    public async Task ImportAsync_Pdf_ConnectsToSourceNode()
    {
        var (sut, upserted) = CreateSut(pdfText: "Extracted PDF content");

        var result = await sut.ImportAsync("report.pdf", Bytes(), domain: "manuals");

        result.Failed.Should().Be(0);
        upserted.Should().Contain(r => r.Id == "upload:manuals");
        upserted.Single(r => r.Id == "upload:manuals:report").Body.Should().Contain("Extracted PDF content");
    }

    [Fact]
    public async Task ImportAsync_FileItems_AreTaggedImported()
    {
        var (sut, upserted) = CreateSut();

        await sut.ImportAsync("guide.md", Bytes("# Guide"), domain: "docs");

        upserted.Single(r => r.Id == "upload:docs:guide").Tags.Should().Contain("imported");
    }

    [Fact]
    public async Task ImportAsync_UnsupportedExtension_ReturnsFailed_WithoutUpsert()
    {
        var (sut, upserted) = CreateSut();

        var result = await sut.ImportAsync("data.xlsx", Bytes(), domain: null);

        result.Failed.Should().Be(1);
        upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_EmptyZip_ReturnsFailed()
    {
        var (sut, upserted) = CreateSut(archiveEntries: []);

        var result = await sut.ImportAsync("empty.zip", Bytes(), domain: null);

        result.Failed.Should().Be(1);
        upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_JsonBundle_UpsertsRulesFaithfully()
    {
        var (sut, upserted) = CreateSut();
        var bundle = new KnowledgeBundle
        {
            Rules =
            [
                new KnowledgeRule { Id = "my-rule", Type = "Guideline", Domain = "coding", Priority = RulePriority.High, Body = "Body" },
            ],
        };
        var json = JsonSerializer.Serialize(bundle, KnowledgeBundleSerialization.Options);

        var result = await sut.ImportAsync("export.json", Encoding.UTF8.GetBytes(json), domain: null);

        result.Imported.Should().Be(1);
        upserted.Single().Id.Should().Be("my-rule");
        upserted.Single().Priority.Should().Be(RulePriority.High);
    }

    [Fact]
    public async Task ImportAsync_InvalidJson_ReturnsFailed()
    {
        var (sut, _) = CreateSut();

        var result = await sut.ImportAsync("bad.json", Bytes("{ not valid ]"), domain: null);

        result.Failed.Should().Be(1);
    }

    [Fact]
    public async Task ImportAsync_Csv_MapsRowsToConnectedItems()
    {
        var (sut, upserted) = CreateSut();

        var result = await sut.ImportAsync("data.csv", Bytes("id,title,body\n1,First,Hello\n2,Second,World"), domain: "notes");

        result.Failed.Should().Be(0);
        upserted.Select(r => r.Id).Should().Contain(new[] { "upload:notes", "upload:notes:1", "upload:notes:2" });
        upserted.Single(r => r.Id == "upload:notes:1").Body.Should().Be("Hello");
    }

    [Fact]
    public async Task ImportAsync_Jsonl_MapsLinesToItems()
    {
        var (sut, upserted) = CreateSut();

        var result = await sut.ImportAsync("dump.jsonl", Bytes("{\"id\":\"a\",\"text\":\"Alpha\"}\n{\"id\":\"b\",\"text\":\"Beta\"}"), domain: "vec");

        result.Failed.Should().Be(0);
        upserted.Select(r => r.Id).Should().Contain(new[] { "upload:vec:a", "upload:vec:b" });
        upserted.Single(r => r.Id == "upload:vec:a").Body.Should().Be("Alpha");
    }

    [Fact]
    public async Task ImportAsync_JsonArray_MapsDocumentsToItems()
    {
        var (sut, upserted) = CreateSut();

        var result = await sut.ImportAsync("chroma.json", Bytes("[{\"id\":\"d1\",\"document\":\"Doc text\",\"metadata\":{}}]"), domain: "kb");

        result.Failed.Should().Be(0);
        upserted.Single(r => r.Id == "upload:kb:d1").Body.Should().Be("Doc text");
    }

    [Fact]
    public async Task ImportAsync_Html_StripsTagsToConnectedItem()
    {
        var (sut, upserted) = CreateSut();

        var result = await sut.ImportAsync(
            "page.html", Bytes("<html><body><h1>Title</h1><p>Hello <b>world</b></p></body></html>"), domain: "wiki");

        result.Failed.Should().Be(0);
        upserted.Should().Contain(r => r.Id == "upload:wiki");
        upserted.Single(r => r.Id == "upload:wiki:page").Body.Should().Contain("Hello world");
    }

    [Fact]
    public async Task ImportAsync_WithChunkStyle_SetsStyleOnContentRules()
    {
        var (sut, upserted) = CreateSut();

        await sut.ImportAsync("guide.md", Bytes("# Guide\n\nText."), domain: "docs", chunkStyle: "code");

        upserted.Single(r => r.Id == "upload:docs:guide").ChunkStyle.Should().Be("code");
    }

    [Fact]
    public async Task ImportAsync_AutoOrUnknownChunkStyle_LeavesStyleNull()
    {
        var (sut, upserted) = CreateSut();

        await sut.ImportAsync("guide.md", Bytes("# Guide"), domain: "docs", chunkStyle: "auto");

        upserted.Single(r => r.Id == "upload:docs:guide").ChunkStyle.Should().BeNull();
    }
}
