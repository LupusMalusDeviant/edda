using Edda.AKG.Chunking;
using Edda.AKG.Embeddings;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.AKG.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="Neo4jEmbeddingCache.EnsureVectorIndexAsync"/>: it provisions the native
/// vector index sized to the embedding dimensionality, skips when no dimensions are known, and
/// swallows provider errors (graceful fallback to app-side cosine).
/// </summary>
public class Neo4jEmbeddingCacheTests
{
    private static Neo4jEmbeddingCache Cache(
        ICypherExecutor cypher, IEmbeddingService embeddings, TimeProvider? timeProvider = null,
        Func<string>? fingerprint = null)
        => new(cypher, embeddings, new AdaptiveDocumentChunker(), () => new ChunkingOptions(),
            NullLogger<Neo4jEmbeddingCache>.Instance, timeProvider: timeProvider, embeddingFingerprint: fingerprint);

    /// <summary>
    /// Drives a rebuild that is awaiting <see cref="FakeTimeProvider"/>-backed retry delays to completion by
    /// advancing the fake clock in a loop — so a backoff-driven test spends no real wall-clock time.
    /// </summary>
    private static async Task DriveToCompletionAsync(Task rebuild, FakeTimeProvider time)
    {
        while (!rebuild.IsCompleted)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            await Task.Yield();
        }

        await rebuild; // surface any exception the rebuild itself threw
    }

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

        var query = cypher.ExecutedWriteQueries.Should()
            .ContainSingle(q => q.Contains("CREATE VECTOR INDEX chunk_embeddings")).Which;
        query.Should().Contain("RuleChunk");
        query.Should().Contain("768");
        query.Should().Contain("cosine");
        // B2: the built dimension is recorded on a meta node so a later change is detectable.
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("EddaEmbeddingMeta"));
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
    public async Task RebuildAsync_TransientProviderError_RetriesWithBackoffThenEmbeds()
    {
        var cypher = SingleRuleCypher();
        var time = new FakeTimeProvider();
        var embedding = EmbeddingsWithDimensions(3);
        // Two transient failures, then success — the in-call backoff must ride them out, not give up.
        embedding
            .SetupSequence(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("test", "rate limited", statusCode: 429))
            .ThrowsAsync(new ProviderException("test", "rate limited", statusCode: 503))
            .ReturnsAsync([[0.1f, 0.2f, 0.3f]]);

        var sut = Cache(cypher, embedding.Object, time);

        await DriveToCompletionAsync(sut.RebuildAsync(CancellationToken.None), time);

        // 1 initial call + 2 retries, then the rule embeds successfully and no failure is booked.
        embedding.Verify(
            e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        sut.EmbeddedSoFar.Should().Be(1);
        cypher.ExecutedWriteQueries.Should().NotContain(q => q.Contains("embedAttempts = coalesce"));
    }

    [Fact]
    public async Task RebuildAsync_PersistentTransientError_GivesUpAfterMaxRetriesAndRecordsFailure()
    {
        var cypher = SingleRuleCypher();
        var time = new FakeTimeProvider();
        var embedding = EmbeddingsWithDimensions(3);
        embedding
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderException("test", "still rate limited", statusCode: 503));

        var sut = Cache(cypher, embedding.Object, time);

        await DriveToCompletionAsync(sut.RebuildAsync(CancellationToken.None), time);

        // 1 initial call + 3 retries (MaxTransientRetries) = 4, then it gives up and records the attempt.
        embedding.Verify(
            e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
        sut.EmbeddedSoFar.Should().Be(0);
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("embedAttempts = coalesce"));
    }

    [Fact]
    public async Task RebuildAsync_ProviderAuthError_DoesNotRetry_RecordsFailureImmediately()
    {
        var cypher = SingleRuleCypher();
        var time = new FakeTimeProvider();
        var embedding = EmbeddingsWithDimensions(3);
        // Auth failures are not transient — retrying is futile, so the attempt is recorded at once.
        embedding
            .Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProviderAuthException("test"));

        var sut = Cache(cypher, embedding.Object, time);

        // No backoff is scheduled for a non-transient failure, so the rebuild completes without clock driving.
        await sut.RebuildAsync(CancellationToken.None);

        embedding.Verify(
            e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sut.EmbeddedSoFar.Should().Be(0);
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("embedAttempts = coalesce"));
    }

    [Fact]
    public async Task RebuildAsync_EmbeddingDimensionChanged_RecreatesIndexAndReembedsWithNewFingerprint()
    {
        var cypher = new FakeCypherExecutor();
        // The rule-selection query returns one rule (fresh in build 1, stale-by-fingerprint in build 2 — the
        // fake does not evaluate the CASE logic, so returning the rule simulates "needs (re)embedding").
        cypher.AddQueryHandler(q => q.Contains("bodyHash IS NULL")
            ? new IReadOnlyDictionary<string, object?>[]
              { new Dictionary<string, object?> { ["id"] = "r1", ["body"] = "Alpha", ["chunkStyle"] = null } }
            : cypher.DefaultResult);

        // ── Build 1: provider A, dimension 4 ──
        var emb4 = EmbeddingsWithDimensions(4);
        emb4.Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => new[] { 0.1f, 0.2f, 0.3f, 0.4f }).ToList());

        await Cache(cypher, emb4.Object, fingerprint: () => "fake:A:4").RebuildAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().Contain(
            q => q.Contains("CREATE VECTOR INDEX chunk_embeddings") && q.Contains("4"));
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("embeddingFingerprint"));
        cypher.ExecutedWriteQueries.Should().NotContain(q => q.Contains("DROP INDEX"),
            because: "the first build has no prior dimension to differ from");

        // ── Build 2: provider B, dimension 8 (the stored index dimension is 4) ──
        cypher.ExecutedWriteQueries.Clear();
        cypher.AddQueryHandler(q => q.Contains("EddaEmbeddingMeta")
            ? new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["dimensions"] = 4L } }
            : cypher.DefaultResult);

        var emb8 = EmbeddingsWithDimensions(8);
        emb8.Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, CancellationToken _) =>
                texts.Select(_ => new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f }).ToList());

        await Cache(cypher, emb8.Object, fingerprint: () => "fake:B:8").RebuildAsync(CancellationToken.None);

        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("DROP INDEX chunk_embeddings"),
            because: "a dimension change drops the stale-dimension index before recreating it");
        cypher.ExecutedWriteQueries.Should().Contain(
            q => q.Contains("CREATE VECTOR INDEX chunk_embeddings") && q.Contains("8"));
        cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("embeddingFingerprint"),
            because: "the stale chunk is re-embedded (and re-fingerprinted) under the new provider");
    }

    /// <summary>Builds a cypher executor whose "rules to embed" query returns a single healthy rule.</summary>
    private static FakeCypherExecutor SingleRuleCypher()
    {
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(q => q.Contains("bodyHash IS NULL")
            ? new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["id"] = "r1", ["body"] = "Alpha body", ["chunkStyle"] = null },
            }
            : cypher.DefaultResult);
        return cypher;
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
