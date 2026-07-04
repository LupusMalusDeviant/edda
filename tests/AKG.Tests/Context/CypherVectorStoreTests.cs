using Edda.AKG.Context;
using Edda.Core.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Context;

/// <summary>Unit tests for <see cref="CypherVectorStore"/> (ICypherExecutor mocked).</summary>
public class CypherVectorStoreTests
{
    private readonly Mock<ICypherExecutor> _cypher = new();
    private readonly CypherVectorStore _sut;

    public CypherVectorStoreTests() => _sut = new CypherVectorStore(_cypher.Object);

    private void SetupQuery(params IReadOnlyDictionary<string, object?>[] rows)
        => _cypher.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(rows);

    [Fact]
    public async Task SearchByVectorAsync_MapsParentScores()
    {
        SetupQuery(
            new Dictionary<string, object?> { ["id"] = "r1", ["score"] = 0.9 },
            new Dictionary<string, object?> { ["id"] = "r2", ["score"] = 0.7 });

        var result = await _sut.SearchByVectorAsync([1f, 0f], topK: 10, threshold: 0.5, userId: "u1");

        result.Should().BeEquivalentTo(new Dictionary<string, double> { ["r1"] = 0.9, ["r2"] = 0.7 });
    }

    [Fact]
    public async Task SearchByVectorAsync_KeepsMaxScorePerParent()
    {
        SetupQuery(
            new Dictionary<string, object?> { ["id"] = "r1", ["score"] = 0.6 },
            new Dictionary<string, object?> { ["id"] = "r1", ["score"] = 0.8 });

        var result = await _sut.SearchByVectorAsync([1f], 10, 0.5, null);

        result["r1"].Should().Be(0.8);
    }

    [Fact]
    public async Task GetRepresentativeEmbeddingsAsync_EmptyIds_SkipsQuery()
    {
        var result = await _sut.GetRepresentativeEmbeddingsAsync([]);

        result.Should().BeEmpty();
        _cypher.Verify(
            c => c.QueryAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRepresentativeEmbeddingsAsync_MapsEmbeddingPerRule()
    {
        SetupQuery(new Dictionary<string, object?> { ["id"] = "r1", ["emb"] = new object[] { 0.1, 0.2 } });

        var result = await _sut.GetRepresentativeEmbeddingsAsync(new[] { "r1" });

        result["r1"].Should().Equal(0.1f, 0.2f);
    }

    [Fact]
    public async Task GetChunkEmbeddingsAsync_MapsAllChunksPerRule()
    {
        SetupQuery(new Dictionary<string, object?>
        {
            ["id"] = "r1",
            ["embs"] = new object[] { new object[] { 0.1, 0.2 }, new object[] { 0.3, 0.4 } },
        });

        var result = await _sut.GetChunkEmbeddingsAsync(new[] { "r1" });

        result["r1"].Should().HaveCount(2);
        result["r1"][0].Should().Equal(0.1f, 0.2f);
        result["r1"][1].Should().Equal(0.3f, 0.4f);
    }
}
