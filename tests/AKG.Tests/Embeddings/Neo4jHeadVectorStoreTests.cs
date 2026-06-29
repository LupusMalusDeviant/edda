using Edda.AKG.Embeddings;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="Neo4jHeadVectorStore"/>: index provisioning, head-vector rebuild from
/// subtree chunk embeddings, and top-k head ranking via the vector index plus the app-side cosine fallback.
/// </summary>
public class Neo4jHeadVectorStoreTests
{
    private static Mock<IEmbeddingService> Embeddings(int dimensions)
    {
        var emb = new Mock<IEmbeddingService>();
        emb.SetupGet(e => e.Dimensions).Returns(dimensions);
        emb.SetupGet(e => e.IsAvailable).Returns(dimensions > 0);
        return emb;
    }

    private static Neo4jHeadVectorStore Store(ICypherExecutor cypher, IEmbeddingService embeddings)
        => new(cypher, embeddings, NullLogger<Neo4jHeadVectorStore>.Instance);

    [Fact]
    public async Task EnsureIndexAsync_PositiveDimensions_CreatesHeadVectorIndex()
    {
        var cypher = new FakeCypherExecutor();

        await Store(cypher, Embeddings(768).Object).EnsureIndexAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().ContainSingle();
        var query = cypher.ExecutedWriteQueries[0];
        query.Should().Contain("CREATE VECTOR INDEX head_embeddings");
        query.Should().Contain("HeadVector");
        query.Should().Contain("768");
    }

    [Fact]
    public async Task EnsureIndexAsync_ZeroDimensions_DoesNothing()
    {
        var cypher = new FakeCypherExecutor();

        await Store(cypher, Embeddings(0).Object).EnsureIndexAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task RebuildAsync_EmbeddingsUnavailable_DoesNothing()
    {
        var cypher = new FakeCypherExecutor();

        await Store(cypher, Embeddings(0).Object).RebuildAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task RebuildAsync_HeadWithChunks_PersistsHeadVectors()
    {
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(q =>
            q.Contains("split(r.id")
                ? new IReadOnlyDictionary<string, object?>[]
                  { new Dictionary<string, object?> { ["id"] = "git:edda", ["ownerId"] = null } }
            : q.Contains("c.parentId STARTS WITH")
                ? new IReadOnlyDictionary<string, object?>[]
                  {
                      new Dictionary<string, object?> { ["emb"] = new List<object> { 1f, 0f, 0f } },
                      new Dictionary<string, object?> { ["emb"] = new List<object> { 0.9f, 0.1f, 0f } },
                  }
            : cypher.DefaultResult);

        await Store(cypher, Embeddings(3).Object).RebuildAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("DETACH DELETE") && q.Contains("HeadVector"));
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("CREATE (:HeadVector"));
    }

    [Fact]
    public async Task FindTopHeadsAsync_EmptyQuery_ReturnsEmpty()
    {
        var cypher = new FakeCypherExecutor();

        var result = await Store(cypher, Embeddings(3).Object)
            .FindTopHeadsAsync([], 3, 0.4, null, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindTopHeadsAsync_RanksByScoreAndRespectsTopK()
    {
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(q =>
            q.Contains("db.index.vector.queryNodes")
                ? new IReadOnlyDictionary<string, object?>[]
                  {
                      new Dictionary<string, object?> { ["id"] = "git:a", ["score"] = 0.9 },
                      new Dictionary<string, object?> { ["id"] = "git:b", ["score"] = 0.7 },
                      new Dictionary<string, object?> { ["id"] = "git:c", ["score"] = 0.5 },
                  }
            : cypher.DefaultResult);

        var result = await Store(cypher, Embeddings(3).Object)
            .FindTopHeadsAsync([1f, 0f, 0f], 2, 0.4, null, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].HeadId.Should().Be("git:a");
        result[0].Score.Should().BeApproximately(0.9, 1e-9);
        result[1].HeadId.Should().Be("git:b");
    }

    [Fact]
    public async Task FindTopHeadsAsync_IndexUnavailable_UsesAppSideCosine()
    {
        var cypher = new Mock<ICypherExecutor>();
        cypher.Setup(c => c.QueryAsync(
                It.Is<string>(q => q.Contains("db.index.vector.queryNodes")),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("no vector index"));
        cypher.Setup(c => c.QueryAsync(
                It.Is<string>(q => q.Contains("MATCH (h:HeadVector)")),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new IReadOnlyDictionary<string, object?>[]
              {
                  new Dictionary<string, object?> { ["id"] = "git:x", ["emb"] = new List<object> { 1f, 0f, 0f } },
                  new Dictionary<string, object?> { ["id"] = "git:y", ["emb"] = new List<object> { 0f, 1f, 0f } },
              });

        var result = await Store(cypher.Object, Embeddings(3).Object)
            .FindTopHeadsAsync([1f, 0f, 0f], 3, 0.4, null, CancellationToken.None);

        // Query [1,0,0] equals git:x (cosine 1.0), orthogonal to git:y (cosine 0 → below threshold).
        result.Should().ContainSingle();
        result[0].HeadId.Should().Be("git:x");
        result[0].Score.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public async Task GetCoverageAsync_ReturnsHeadsWithVectorsAndTotal()
    {
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(q => q.Contains("count(CASE WHEN vectors > 0")
            ? new IReadOnlyDictionary<string, object?>[]
              { new Dictionary<string, object?> { ["totalHeads"] = 5L, ["withVectors"] = 3L } }
            : cypher.DefaultResult);

        var (withVectors, total) = await Store(cypher, Embeddings(3).Object)
            .GetCoverageAsync(CancellationToken.None);

        withVectors.Should().Be(3);
        total.Should().Be(5);
    }

    [Fact]
    public async Task RebuildAsync_HeadWithoutChunks_PersistsNoCentroids()
    {
        var cypher = new FakeCypherExecutor();
        // Head is returned, but its subtree-embeddings query falls through to the empty default.
        cypher.AddQueryHandler(q =>
            q.Contains("split(r.id")
                ? new IReadOnlyDictionary<string, object?>[]
                  { new Dictionary<string, object?> { ["id"] = "git:empty", ["ownerId"] = null } }
            : cypher.DefaultResult);

        await Store(cypher, Embeddings(3).Object).RebuildAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().NotContain(q => q.Contains("CREATE (:HeadVector"));
    }
}
