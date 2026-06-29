using Edda.AKG.Chunking;
using Edda.AKG.Embeddings;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="Neo4jEmbeddingCache.EnsureVectorIndexAsync"/>: it provisions the native
/// vector index sized to the embedding dimensionality, skips when no dimensions are known, and
/// swallows provider errors (graceful fallback to app-side cosine).
/// </summary>
public class Neo4jEmbeddingCacheTests
{
    private static Neo4jEmbeddingCache Cache(ICypherExecutor cypher, IEmbeddingService embeddings)
        => new(cypher, embeddings, new AdaptiveDocumentChunker(), () => new ChunkingOptions(),
            NullLogger<Neo4jEmbeddingCache>.Instance);

    private static Mock<IEmbeddingService> EmbeddingsWithDimensions(int dimensions)
    {
        var emb = new Mock<IEmbeddingService>();
        emb.SetupGet(e => e.Dimensions).Returns(dimensions);
        emb.SetupGet(e => e.IsAvailable).Returns(dimensions > 0);
        return emb;
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_PositiveDimensions_IssuesCreateVectorIndexWithDimension()
    {
        var cypher = new FakeCypherExecutor();
        var sut = Cache(cypher, EmbeddingsWithDimensions(768).Object);

        await sut.EnsureVectorIndexAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().ContainSingle();
        var query = cypher.ExecutedWriteQueries[0];
        query.Should().Contain("CREATE VECTOR INDEX chunk_embeddings");
        query.Should().Contain("RuleChunk");
        query.Should().Contain("768");
        query.Should().Contain("cosine");
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_ZeroDimensions_DoesNothing()
    {
        var cypher = new FakeCypherExecutor();
        var sut = Cache(cypher, EmbeddingsWithDimensions(0).Object);

        await sut.EnsureVectorIndexAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureVectorIndexAsync_ProviderThrows_SwallowsException()
    {
        var cypher = new Mock<ICypherExecutor>();
        cypher.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("vector index unsupported"));
        var sut = Cache(cypher.Object, EmbeddingsWithDimensions(768).Object);

        var act = async () => await sut.EnsureVectorIndexAsync(CancellationToken.None);

        await act.Should().NotThrowAsync(
            because: "vector-index provisioning is best-effort and must not break startup");
    }

    [Fact]
    public async Task RebuildAsync_EmbedsEveryRule()
    {
        var cypher = new FakeCypherExecutor();
        // The "find rules to embed" query returns three rules; everything else uses the empty default.
        cypher.AddQueryHandler(q => q.Contains("bodyHash IS NULL")
            ? new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["id"] = "r1", ["body"] = "Alpha body", ["chunkStyle"] = null },
                new Dictionary<string, object?> { ["id"] = "r2", ["body"] = "Beta body", ["chunkStyle"] = null },
                new Dictionary<string, object?> { ["id"] = "r3", ["body"] = "Gamma body", ["chunkStyle"] = null },
            }
            : cypher.DefaultResult);

        var embedding = EmbeddingsWithDimensions(3);
        embedding
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToList());

        var sut = Cache(cypher, embedding.Object);

        await sut.RebuildAsync(CancellationToken.None);

        sut.EmbeddedSoFar.Should().Be(3);
        sut.IsRebuilding.Should().BeFalse();
        embedding.Verify(
            e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task RebuildAsync_OneRuleFails_EmbedsOthersAndRecordsFailure()
    {
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(q => q.Contains("bodyHash IS NULL")
            ? new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["id"] = "ok-1", ["body"] = "Alpha", ["chunkStyle"] = null },
                new Dictionary<string, object?> { ["id"] = "bad", ["body"] = "BOOM", ["chunkStyle"] = null },
                new Dictionary<string, object?> { ["id"] = "ok-2", ["body"] = "Beta", ["chunkStyle"] = null },
            }
            : cypher.DefaultResult);

        var embedding = EmbeddingsWithDimensions(3);
        embedding
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToList());
        // The rule whose chunk text contains "BOOM" makes the provider throw (last matching setup wins).
        embedding
            .Setup(e => e.EmbedBatchAsync(
                It.Is<IReadOnlyList<string>>(l => l.Any(t => t.Contains("BOOM"))), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider rejected chunk"));

        var sut = Cache(cypher, embedding.Object);

        await sut.RebuildAsync(CancellationToken.None);

        // The two healthy rules embed; the single failure does not abort the whole run.
        sut.EmbeddedSoFar.Should().Be(2);
        sut.IsRebuilding.Should().BeFalse();
        // The failing rule's attempt is recorded so the backfill eventually gives up instead of looping forever.
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("embedAttempts = coalesce"));
    }

    [Fact]
    public async Task GetCoverageAsync_ParsesEmbeddedPendingFailedTotal()
    {
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(q => q.Contains("count(CASE WHEN chunks > 0")
            ? new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?>
                {
                    ["embedded"] = 139L, ["pending"] = 18_800L, ["failed"] = 61L, ["total"] = 19_000L,
                },
            }
            : cypher.DefaultResult);

        var sut = Cache(cypher, EmbeddingsWithDimensions(3).Object);

        var (embedded, pending, failed, total) = await sut.GetCoverageAsync(CancellationToken.None);

        embedded.Should().Be(139);
        pending.Should().Be(18_800);
        failed.Should().Be(61);
        total.Should().Be(19_000);
    }

    [Fact]
    public async Task RebuildAsync_FileRule_MarksRepositoryHeadDirty()
    {
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(q => q.Contains("bodyHash IS NULL")
            ? new IReadOnlyDictionary<string, object?>[]
              { new Dictionary<string, object?> { ["id"] = "git:edda:docs/x", ["body"] = "Body", ["chunkStyle"] = null } }
            : cypher.DefaultResult);

        var embedding = EmbeddingsWithDimensions(3);
        embedding
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f }).ToList());

        await Cache(cypher, embedding.Object).RebuildAsync(CancellationToken.None);

        // The git:edda:docs/x leaf re-embedded → its repo head git:edda is flagged for centroid rebuild.
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("headVectorDirty = true"));
    }
}
