using Edda.Core.Providers;

namespace Edda.Core.Tests.Providers;

/// <summary>Unit tests for <see cref="ProviderModelParser"/> model-listing response parsing.</summary>
public sealed class ProviderModelParserTests
{
    [Fact]
    public void ParseOllamaTags_ReturnsNamesSortedAndDeduped()
    {
        var json = """{"models":[{"name":"llama3.1:latest"},{"name":"bge-m3:latest"},{"name":"bge-m3:latest"}]}""";

        ProviderModelParser.ParseOllamaTags(json).Should().Equal("bge-m3:latest", "llama3.1:latest");
    }

    [Fact]
    public void ParseOllamaTags_Malformed_ReturnsEmpty()
        => ProviderModelParser.ParseOllamaTags("not json at all").Should().BeEmpty();

    [Fact]
    public void ParseOllamaTags_NoModelsProperty_ReturnsEmpty()
        => ProviderModelParser.ParseOllamaTags("""{"foo":1}""").Should().BeEmpty();

    [Fact]
    public void ParseOpenAiModels_ReturnsIds()
    {
        var json = """{"data":[{"id":"gpt-4o"},{"id":"gpt-4o-mini"}]}""";

        ProviderModelParser.ParseOpenAiModels(json).Should().Equal("gpt-4o", "gpt-4o-mini");
    }

    [Fact]
    public void ParseOpenAiModels_Empty_ReturnsEmpty()
        => ProviderModelParser.ParseOpenAiModels("""{"data":[]}""").Should().BeEmpty();

    [Fact]
    public void ParseGeminiModels_StripsModelsPrefix()
    {
        var json = """{"models":[{"name":"models/gemini-1.5-pro"},{"name":"models/gemini-1.5-flash"}]}""";

        ProviderModelParser.ParseGeminiModels(json).Should().Equal("gemini-1.5-flash", "gemini-1.5-pro");
    }

    [Fact]
    public void ParseGeminiModels_Malformed_ReturnsEmpty()
        => ProviderModelParser.ParseGeminiModels("<html>nope</html>").Should().BeEmpty();
}
