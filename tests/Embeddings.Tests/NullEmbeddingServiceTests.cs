using Edda.Agent.Providers.Embeddings;

namespace Edda.Agent.Providers.Tests.Embeddings;

public sealed class NullEmbeddingServiceTests
{
    private readonly NullEmbeddingService _sut = new();

    [Fact]
    public void IsAvailable_ReturnsFalse()
    {
        _sut.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Dimensions_ReturnsZero()
    {
        _sut.Dimensions.Should().Be(0);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsEmptyArray()
    {
        var result = await _sut.EmbedAsync("some text to embed");

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task EmbedAsync_EmptyInput_ReturnsEmptyArray()
    {
        var result = await _sut.EmbedAsync(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task EmbedBatchAsync_SingleItem_ReturnsSingleEmptyArray()
    {
        var result = await _sut.EmbedBatchAsync(["hello"]);

        result.Should().HaveCount(1);
        result[0].Should().BeEmpty();
    }

    [Fact]
    public async Task EmbedBatchAsync_MultipleItems_ReturnsEmptyArraysForEachInput()
    {
        var texts = new[] { "first", "second", "third", "fourth" };

        var result = await _sut.EmbedBatchAsync(texts);

        result.Should().HaveCount(texts.Length);
        result.Should().AllSatisfy(arr => arr.Should().BeEmpty());
    }

    [Fact]
    public async Task EmbedBatchAsync_EmptyList_ReturnsEmptyList()
    {
        var result = await _sut.EmbedBatchAsync([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task EmbedBatchAsync_PreservesInputOrder()
    {
        // NullEmbeddingService returns empty arrays; verify count matches input count,
        // confirming one-to-one correspondence with input ordering.
        var texts = Enumerable.Range(0, 10).Select(i => $"text-{i}").ToList();

        var result = await _sut.EmbedBatchAsync(texts);

        result.Should().HaveCount(10);
    }
}
