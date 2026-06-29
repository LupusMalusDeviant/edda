using Edda.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Embeddings.Tests;

/// <summary>Unit tests for <see cref="EmbeddingProviderFactory"/> provider selection.</summary>
public sealed class EmbeddingProviderFactoryTests
{
    private static EmbeddingProviderFactory CreateSut()
        => new(Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, TimeProvider.System);

    [Fact]
    public void Create_Openai_IsAvailable_WithKnownDimensions()
    {
        var service = CreateSut().Create(new EmbeddingProviderConfig { Provider = "openai" });

        service.IsAvailable.Should().BeTrue();
        service.Dimensions.Should().Be(1536);
    }

    [Fact]
    public void Create_Null_IsUnavailable()
        => CreateSut().Create(new EmbeddingProviderConfig { Provider = "null" }).IsAvailable.Should().BeFalse();

    [Fact]
    public void Create_UnknownProvider_FallsBackToNull()
        => CreateSut().Create(new EmbeddingProviderConfig { Provider = "does-not-exist" }).IsAvailable.Should().BeFalse();

    [Theory]
    [InlineData("google")]
    [InlineData("voyage")]
    [InlineData("ollama")]
    [InlineData("custom")]
    [InlineData("bedrock")]
    [InlineData("aws")]
    public void Create_KnownProvider_IsAvailable(string provider)
        => CreateSut().Create(new EmbeddingProviderConfig { Provider = provider }).IsAvailable.Should().BeTrue();

    [Fact]
    public void Create_Bedrock_UsesTitanV2DefaultDimensions()
        => CreateSut().Create(new EmbeddingProviderConfig { Provider = "bedrock" }).Dimensions.Should().Be(1024);

    [Fact]
    public void Create_Bedrock_TitanV1Model_Uses1536Dimensions()
        => CreateSut().Create(new EmbeddingProviderConfig
        {
            Provider = "bedrock",
            Model = "amazon.titan-embed-text-v1",
        }).Dimensions.Should().Be(1536);

    [Fact]
    public void Create_Bedrock_RespectsExplicitDimensions()
        => CreateSut().Create(new EmbeddingProviderConfig
        {
            Provider = "bedrock",
            Dimensions = 256,
        }).Dimensions.Should().Be(256);
}
