using Edda.AKG.Ingestion.Llm;
using Edda.Core.Exceptions;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Llm;

/// <summary>Unit tests for <see cref="LlmChatClientFactory"/> provider selection.</summary>
public sealed class LlmChatClientFactoryTests
{
    private static LlmChatClientFactory CreateSut() => new(Mock.Of<IHttpClientFactory>());

    [Theory]
    [InlineData("anthropic", "anthropic")]
    [InlineData("openai", "openai")]
    [InlineData("openrouter", "openrouter")]
    [InlineData("ollama", "ollama")]
    [InlineData("gemini", "gemini")]
    [InlineData("bedrock", "bedrock")]
    [InlineData("custom", "custom")]
    public void Create_KnownProvider_ReturnsClientWithMatchingProviderName(string provider, string expected)
    {
        var sut = CreateSut();

        var client = sut.Create(new LlmProviderConfig { Provider = provider });

        client.ProviderName.Should().Be(expected);
    }

    [Fact]
    public void Create_ProviderName_IsCaseInsensitive()
    {
        var sut = CreateSut();

        sut.Create(new LlmProviderConfig { Provider = "Anthropic" }).ProviderName.Should().Be("anthropic");
    }

    [Fact]
    public void Create_UnknownProvider_ThrowsProviderException()
    {
        var sut = CreateSut();

        var act = () => sut.Create(new LlmProviderConfig { Provider = "does-not-exist" });

        act.Should().Throw<ProviderException>();
    }
}
