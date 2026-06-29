using Edda.AKG.Chunking;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Chunking;

/// <summary>
/// Behavioural unit tests for <see cref="AdaptiveDocumentChunker"/>: single-chunk fast paths, size limits,
/// losslessness, and style-adaptive handling of prose, Markdown, fenced code, tables and code files.
/// </summary>
public sealed class AdaptiveDocumentChunkerTests
{
    private static readonly AdaptiveDocumentChunker Sut = new();

    private static ChunkingOptions Options(int max = 200, int overlap = 20, bool enabled = true)
        => new() { MaxChars = max, OverlapChars = overlap, Enabled = enabled };

    [Fact]
    public void Chunk_ShortBody_ReturnsSingleChunk()
    {
        var result = Sut.Chunk("short text", Options());

        result.Should().ContainSingle();
        result[0].Ordinal.Should().Be(0);
        result[0].Text.Should().Be("short text");
    }

    [Fact]
    public void Chunk_Disabled_ReturnsSingleChunkEvenWhenLarge()
    {
        var body = new string('a', 5000);

        var result = Sut.Chunk(body, Options(enabled: false));

        result.Should().ContainSingle();
        result[0].Text.Should().Be(body);
    }

    [Fact]
    public void Chunk_NullOrEmpty_ReturnsSingleChunk()
    {
        Sut.Chunk(string.Empty, Options()).Should().ContainSingle();
        Sut.Chunk(null!, Options()).Should().ContainSingle();
    }

    [Fact]
    public void Chunk_LongProse_SplitsIntoSequentialChunksWithinLimit()
    {
        var prose = string.Join(
            "\n\n",
            Enumerable.Range(0, 40).Select(i => $"Paragraph number {i} with some filler words to add length."));

        var result = Sut.Chunk(prose, Options(max: 200, overlap: 20));

        result.Count.Should().BeGreaterThan(1);
        result.Should().OnlyContain(c => c.Text.Length <= 200);
        result.Select(c => c.Ordinal).Should().Equal(Enumerable.Range(0, result.Count));
    }

    [Fact]
    public void Chunk_ZeroOverlap_PreservesAllContent()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 30).Select(i => $"Sentence {i}."));

        var result = Sut.Chunk(text, Options(max: 120, overlap: 0));

        string.Concat(result.Select(c => c.Text)).Should().Be(text);
    }

    [Fact]
    public void Chunk_FencedCodeBlock_KeptIntactWhenItFits()
    {
        var doc = "Intro paragraph.\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n\nOutro paragraph.";

        var result = Sut.Chunk(doc, Options(max: 60, overlap: 0));

        result.Should().Contain(c => c.Text.Contains("```csharp\nvar x = 1;\nvar y = 2;\n```"));
    }

    [Fact]
    public void Chunk_LargeTable_RepeatsHeaderAndUsesTableStyle()
    {
        var header = "| id | name |\n|----|------|\n";
        var rows = string.Concat(Enumerable.Range(0, 40).Select(i => $"| {i} | name-{i} |\n"));

        var result = Sut.Chunk(header + rows, Options(max: 120, overlap: 0), fileNameHint: "data.md");

        result.Count.Should().BeGreaterThan(1);
        result.Should().OnlyContain(c => c.Text.Contains("| id | name |"));
        result.Should().OnlyContain(c => c.Style == "table");
    }

    [Fact]
    public void Chunk_CodeFile_UsesCodeStyleAndSplits()
    {
        var code = string.Join(
            "\n", Enumerable.Range(0, 60).Select(i => $"public void Method{i}() {{ DoWork({i}); }}"));

        var result = Sut.Chunk(code, Options(max: 200), fileNameHint: "Service.cs");

        result.Should().OnlyContain(c => c.Style == "code");
        result.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Chunk_ForcedStyle_OverridesDetection()
    {
        var result = Sut.Chunk("just some plain prose without structure", Options(), fileNameHint: null, forcedStyle: "code");

        result.Should().OnlyContain(c => c.Style == "code");
    }

    [Fact]
    public void Chunk_UnknownForcedStyle_FallsBackToDetection()
    {
        var result = Sut.Chunk("# Heading\n\nsome text", Options(), forcedStyle: "bogus");

        result[0].Style.Should().Be("markdown");
    }
}
