using Edda.AKG.Chunking;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Edda.AKG.Tests.Chunking;

/// <summary>Unit tests for <see cref="DocumentStyleDetector"/>.</summary>
public sealed class DocumentStyleDetectorTests
{
    [Theory]
    [InlineData("Service.cs")]
    [InlineData("script.py")]
    [InlineData("app.ts")]
    public void Detect_CodeExtension_ReturnsCode(string hint)
        => DocumentStyleDetector.Detect("anything at all", hint).Should().Be(DocumentStyle.Code);

    [Fact]
    public void Detect_PipeTable_ReturnsTable()
        => DocumentStyleDetector.Detect("| a | b |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |", null)
            .Should().Be(DocumentStyle.Table);

    [Fact]
    public void Detect_Headings_ReturnsMarkdown()
        => DocumentStyleDetector.Detect("# Title\n\nSome text.", null).Should().Be(DocumentStyle.Markdown);

    [Fact]
    public void Detect_CodeSignalsWithoutHint_ReturnsCode()
        => DocumentStyleDetector.Detect("import os\nimport sys\ndef main():\n    work()\nclass Foo:\n    bar()", null)
            .Should().Be(DocumentStyle.Code);

    [Fact]
    public void Detect_PlainText_ReturnsProse()
        => DocumentStyleDetector.Detect("Just some ordinary sentences without any structure here.", null)
            .Should().Be(DocumentStyle.Prose);

    [Fact]
    public void Detect_Empty_ReturnsProse()
        => DocumentStyleDetector.Detect(string.Empty, null).Should().Be(DocumentStyle.Prose);

    [Theory]
    [InlineData("prose", "Prose")]
    [InlineData("CODE", "Code")]
    [InlineData(" markdown ", "Markdown")]
    [InlineData("table", "Table")]
    public void TryParse_KnownStyle_Parses(string input, string expected)
        => DocumentStyleDetector.TryParse(input)!.ToString().Should().Be(expected);

    [Theory]
    [InlineData("auto")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_UnknownOrBlank_ReturnsNull(string? input)
        => DocumentStyleDetector.TryParse(input).Should().BeNull();
}

/// <summary>Unit tests for <see cref="RecursiveTextSplitter"/>.</summary>
public sealed class RecursiveTextSplitterTests
{
    private static readonly string[] Separators = ["\n\n", "\n", ". ", " ", ""];

    [Fact]
    public void Split_WithinLimit_ReturnsSingle()
        => RecursiveTextSplitter.Split("small", 100, 0, Separators).Should().ContainSingle();

    [Fact]
    public void Split_AllChunksWithinLimit()
    {
        var text = string.Join(" ", Enumerable.Range(0, 200).Select(i => $"word{i}"));

        var chunks = RecursiveTextSplitter.Split(text, 100, 10, Separators);

        chunks.Count.Should().BeGreaterThan(1);
        chunks.Should().OnlyContain(c => c.Length <= 100);
    }

    [Fact]
    public void Split_ZeroOverlap_IsLossless()
    {
        var text = string.Join("\n", Enumerable.Range(0, 50).Select(i => $"line {i}"));

        string.Concat(RecursiveTextSplitter.Split(text, 60, 0, Separators)).Should().Be(text);
    }

    [Fact]
    public void Split_NoSeparatorPresent_HardWrapsLosslessly()
    {
        var text = new string('x', 250);

        var chunks = RecursiveTextSplitter.Split(text, 100, 0, ["\n\n"]);

        chunks.Should().OnlyContain(c => c.Length <= 100);
        string.Concat(chunks).Should().Be(text);
    }
}

/// <summary>Unit tests for <see cref="TableSplitter"/>.</summary>
public sealed class TableSplitterTests
{
    [Fact]
    public void Split_LargeTable_RepeatsHeaderInEveryPiece()
    {
        var header = "| id | name |\n|----|------|\n";
        var rows = string.Concat(Enumerable.Range(0, 20).Select(i => $"| {i} | n{i} |\n"));

        var pieces = TableSplitter.Split(header + rows, 80);

        pieces.Count.Should().BeGreaterThan(1);
        pieces.Should().OnlyContain(p => p.StartsWith("| id | name |"));
    }

    [Fact]
    public void Split_HeaderOnly_ReturnsInputAsSingle()
        => TableSplitter.Split("| id | name |\n|----|------|\n", 80).Should().ContainSingle();

    [Fact]
    public void Split_NoSeparatorRow_UsesFirstLineAsHeader()
        => TableSplitter.Split("| only header |\n| row1 |\n", 80)
            .Should().OnlyContain(p => p.StartsWith("| only header |"));
}

/// <summary>Unit tests for <see cref="BlockSegmenter"/> and <see cref="LineUtilities"/>.</summary>
public sealed class BlockSegmenterTests
{
    [Fact]
    public void Segment_CodeStyle_ReturnsSingleCodeBlock()
    {
        var blocks = BlockSegmenter.Segment("var x = 1;\nvar y = 2;", DocumentStyle.Code);

        blocks.Should().ContainSingle();
        blocks[0].Kind.Should().Be(BlockKind.Code);
    }

    [Fact]
    public void Segment_MixedMarkdown_IsolatesCodeTableText_Losslessly()
    {
        var doc = "Intro\n\n```\ncode line\n```\ntext\n| a | b |\n| 1 | 2 |\nmore text\n";

        var blocks = BlockSegmenter.Segment(doc, DocumentStyle.Markdown);

        blocks.Should().Contain(b => b.Kind == BlockKind.Code);
        blocks.Should().Contain(b => b.Kind == BlockKind.Table);
        blocks.Should().Contain(b => b.Kind == BlockKind.Text);
        string.Concat(blocks.Select(b => b.Text)).Should().Be(doc);
    }

    [Fact]
    public void Segment_EmptyCode_ReturnsNoBlocks()
        => BlockSegmenter.Segment(string.Empty, DocumentStyle.Code).Should().BeEmpty();

    [Fact]
    public void SplitKeepEndings_PreservesNewlines()
        => LineUtilities.SplitKeepEndings("a\nb\nc").Should().Equal("a\n", "b\n", "c");
}

/// <summary>Unit tests for <see cref="ChunkingOptionsResolver"/>.</summary>
public sealed class ChunkingOptionsResolverTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c[It.IsAny<string>()])
            .Returns((string key) => values.TryGetValue(key, out var value) ? value : null);
        return config.Object;
    }

    [Fact]
    public void Resolve_NothingSet_UsesDefaults()
    {
        var options = ChunkingOptionsResolver.Resolve(new ChunkingSettings(), null);

        options.Enabled.Should().BeTrue();
        options.MaxChars.Should().Be(ChunkingOptions.DefaultMaxChars);
        options.OverlapChars.Should().Be(ChunkingOptions.DefaultOverlapChars);
    }

    [Fact]
    public void Resolve_SettingsWinOverEnvironment()
    {
        var settings = new ChunkingSettings { Enabled = false, MaxChars = 500, OverlapChars = 50 };
        var config = Config(new Dictionary<string, string?>
        {
            ["CHUNKING_ENABLED"] = "true",
            ["CHUNKING_MAX_CHARS"] = "999",
        });

        var options = ChunkingOptionsResolver.Resolve(settings, config);

        options.Enabled.Should().BeFalse();
        options.MaxChars.Should().Be(500);
        options.OverlapChars.Should().Be(50);
    }

    [Fact]
    public void Resolve_EnvironmentFallback_WhenSettingsNull()
    {
        var config = Config(new Dictionary<string, string?>
        {
            ["CHUNKING_ENABLED"] = "false",
            ["CHUNKING_MAX_CHARS"] = "800",
            ["CHUNKING_OVERLAP_CHARS"] = "77",
        });

        var options = ChunkingOptionsResolver.Resolve(new ChunkingSettings(), config);

        options.Enabled.Should().BeFalse();
        options.MaxChars.Should().Be(800);
        options.OverlapChars.Should().Be(77);
    }

    [Fact]
    public void Resolve_NonPositiveMaxChars_FallsBackToDefault()
        => ChunkingOptionsResolver.Resolve(new ChunkingSettings { MaxChars = 0 }, null)
            .MaxChars.Should().Be(ChunkingOptions.DefaultMaxChars);
}
