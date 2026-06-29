using Edda.Agent.Providers.Embeddings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edda.Embeddings.Tests;

/// <summary>Unit tests for <see cref="BedrockEmbeddingService"/> request/response mapping and defaults.</summary>
public sealed class BedrockEmbeddingServiceTests
{
    [Fact]
    public void ParseEmbedding_Titan_ReturnsVector()
        => BedrockEmbeddingService.ParseEmbedding("{\"embedding\":[0.1,0.2,0.3]}", "amazon.titan-embed-text-v2:0")
            .Should().Equal(0.1f, 0.2f, 0.3f);

    [Fact]
    public void ParseEmbedding_Cohere_ReturnsFirstVector()
        => BedrockEmbeddingService.ParseEmbedding("{\"embeddings\":[[0.5,0.6]]}", "cohere.embed-english-v3")
            .Should().Equal(0.5f, 0.6f);

    [Fact]
    public void ParseEmbedding_NoVector_ReturnsEmpty()
        => BedrockEmbeddingService.ParseEmbedding("{\"other\":1}", "amazon.titan-embed-text-v2:0")
            .Should().BeEmpty();

    [Fact]
    public void BuildRequestBody_TitanV2_IncludesInputTextAndDimensions()
    {
        var body = BedrockEmbeddingService.BuildRequestBody("amazon.titan-embed-text-v2:0", "hello", 512);

        body.Should().Contain("\"inputText\":\"hello\"");
        body.Should().Contain("\"dimensions\":512");
    }

    [Fact]
    public void BuildRequestBody_TitanV1_OmitsDimensions()
    {
        var body = BedrockEmbeddingService.BuildRequestBody("amazon.titan-embed-text-v1", "hi", 512);

        body.Should().Contain("\"inputText\":\"hi\"");
        body.Should().NotContain("dimensions");
    }

    [Fact]
    public void BuildRequestBody_Cohere_UsesTextsAndInputType()
    {
        var body = BedrockEmbeddingService.BuildRequestBody("cohere.embed-english-v3", "hello", 0);

        body.Should().Contain("\"texts\":[\"hello\"]");
        body.Should().Contain("\"input_type\":\"search_document\"");
    }

    [Theory]
    [InlineData("amazon.titan-embed-text-v2:0", 1024)]
    [InlineData("amazon.titan-embed-text-v1", 1536)]
    [InlineData("cohere.embed-english-v3", 1024)]
    public void DefaultDimensions_ReturnsModelNativeSize(string model, int expected)
        => BedrockEmbeddingService.DefaultDimensions(model).Should().Be(expected);

    [Fact]
    public void IsAvailable_AlwaysTrue()
        => new BedrockEmbeddingService(
                null, null, "us-east-1", "amazon.titan-embed-text-v2:0", 1024,
                NullLogger<BedrockEmbeddingService>.Instance)
            .IsAvailable.Should().BeTrue();

    [Fact]
    public void Dimensions_ReturnsConfiguredValue()
        => new BedrockEmbeddingService(
                null, null, "us-east-1", "amazon.titan-embed-text-v2:0", 777,
                NullLogger<BedrockEmbeddingService>.Instance)
            .Dimensions.Should().Be(777);
}
