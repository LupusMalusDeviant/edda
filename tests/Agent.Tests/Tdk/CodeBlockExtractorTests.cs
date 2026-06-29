using Edda.Agent.Tdk;

namespace Edda.Agent.Tests.Tdk;

public class CodeBlockExtractorTests
{
    [Fact]
    public void Extract_FencedBlockWithLanguage_ExtractsCorrectly()
    {
        var markdown = "Here is some code:\n```python\nprint('hello')\n```\nDone.";

        var blocks = CodeBlockExtractor.Extract(markdown);

        blocks.Should().HaveCount(1);
        blocks[0].Language.Should().Be("python");
        blocks[0].Code.Should().Be("print('hello')\n");
    }

    [Fact]
    public void Extract_FencedBlockNoLanguage_ExtractsWithEmptyLanguage()
    {
        var markdown = "```\nsome code here\n```";

        var blocks = CodeBlockExtractor.Extract(markdown);

        blocks.Should().HaveCount(1);
        blocks[0].Language.Should().BeEmpty();
        blocks[0].Code.Should().Be("some code here\n");
    }

    [Fact]
    public void Extract_MultipleBlocks_ExtractsAll()
    {
        var markdown = "```python\ncode1\n```\nSome text.\n```csharp\ncode2\n```";

        var blocks = CodeBlockExtractor.Extract(markdown);

        blocks.Should().HaveCount(2);
        blocks[0].Language.Should().Be("python");
        blocks[1].Language.Should().Be("csharp");
    }

    [Fact]
    public void Extract_NoCodeBlocks_ReturnsEmpty()
    {
        var markdown = "This is just plain text with no code blocks.";

        var blocks = CodeBlockExtractor.Extract(markdown);

        blocks.Should().BeEmpty();
    }

    [Fact]
    public void Extract_EmptyInput_ReturnsEmpty()
    {
        var blocks = CodeBlockExtractor.Extract(string.Empty);

        blocks.Should().BeEmpty();
    }

    [Fact]
    public void Extract_LanguageIsCaseNormalized()
    {
        var markdown = "```Python\ncode\n```";

        var blocks = CodeBlockExtractor.Extract(markdown);

        blocks.Should().HaveCount(1);
        blocks[0].Language.Should().Be("python");
    }

    [Fact]
    public void Extract_MultilineCode_PreservesAllLines()
    {
        var markdown = "```python\nline1\nline2\nline3\n```";

        var blocks = CodeBlockExtractor.Extract(markdown);

        blocks[0].Code.Should().Contain("line1");
        blocks[0].Code.Should().Contain("line2");
        blocks[0].Code.Should().Contain("line3");
    }
}
