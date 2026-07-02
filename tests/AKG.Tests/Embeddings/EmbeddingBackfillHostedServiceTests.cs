using Edda.AKG.Embeddings;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="EmbeddingBackfillHostedService"/>: it drains the embedding cache while the
/// provider is available, stays idle when it is not, and aborts cleanly on shutdown.
/// </summary>
public class EmbeddingBackfillHostedServiceTests
{
    private static EmbeddingBackfillHostedService Service(
        INeo4jEmbeddingCache cache, IEmbeddingService embeddings, IHeadVectorStore headVectors)
        => new(cache, headVectors, embeddings, NullLogger<EmbeddingBackfillHostedService>.Instance,
            intervalSeconds: 5, initialDelaySeconds: 0);

    private static Mock<IHeadVectorStore> HeadVectors()
    {
        var mock = new Mock<IHeadVectorStore>();
        mock.Setup(h => h.RebuildAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<IEmbeddingService> Embeddings(bool available)
    {
        var mock = new Mock<IEmbeddingService>();
        mock.SetupGet(e => e.IsAvailable).Returns(available);
        return mock;
    }

    [Fact]
    public async Task StartAsync_EmbeddingsAvailable_RebuildsChunksThenHeadVectors()
    {
        var cache = new FakeEmbeddingCache();
        var heads = HeadVectors();

        var sut = Service(cache, Embeddings(available: true).Object, heads.Object);
        await sut.StartAsync(CancellationToken.None);

        var finished = await Task.WhenAny(cache.Rebuilt, Task.Delay(TimeSpan.FromSeconds(2)));
        await sut.StopAsync(CancellationToken.None);

        finished.Should().Be(cache.Rebuilt,
            because: "the backfill loop must invoke RebuildAsync when the embedding provider is available");
        cache.RebuildCalls.Should().BeGreaterOrEqualTo(1);
        heads.Verify(h => h.RebuildAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "head centroids are recomputed after the chunk-embedding pass");
    }

    [Fact]
    public async Task StartAsync_CacheEmbedsNothing_SkipsHeadVectorRebuild()
    {
        // B9: an idle cycle that (re)embeds no rules leaves every head clean, so the head-vector store
        // must not be touched — otherwise k-means re-clusters unchanged repositories on every backfill tick.
        var cache = new FakeEmbeddingCache { EmbeddedPerCycle = 0 };
        var heads = HeadVectors();

        var sut = Service(cache, Embeddings(available: true).Object, heads.Object);
        await sut.StartAsync(CancellationToken.None);

        // Wait until at least one full backfill cycle has actually run (the cache's RebuildAsync fired).
        await Task.WhenAny(cache.Rebuilt, Task.Delay(TimeSpan.FromSeconds(2)));
        await sut.StopAsync(CancellationToken.None);

        cache.RebuildCalls.Should().BeGreaterOrEqualTo(1);
        heads.Verify(h => h.RebuildAsync(It.IsAny<CancellationToken>()), Times.Never,
            "no chunks were written this cycle, so head centroids need no recomputation");
    }

    [Fact]
    public async Task StartAsync_EmbeddingsUnavailable_DoesNotRebuild()
    {
        var cache = new FakeEmbeddingCache();
        var heads = HeadVectors();

        var sut = Service(cache, Embeddings(available: false).Object, heads.Object);
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        cache.RebuildCalls.Should().Be(0);
        heads.Verify(h => h.RebuildAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_CancelsInFlightRebuild()
    {
        var cache = new FakeEmbeddingCache();

        var sut = Service(cache, Embeddings(available: false).Object, HeadVectors().Object);
        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        cache.CancelCalls.Should().BeGreaterOrEqualTo(1);
    }

    /// <summary>
    /// Hand-written test double for the internal <see cref="INeo4jEmbeddingCache"/>. Moq cannot proxy this
    /// internal interface (the runtime DynamicProxy assembly is unsigned and does not match the signed-key
    /// InternalsVisibleTo grant), so we implement it directly and count the calls we assert on.
    /// </summary>
    private sealed class FakeEmbeddingCache : INeo4jEmbeddingCache
    {
        private readonly TaskCompletionSource _rebuilt = new();
        private int _rebuildCalls;
        private int _cancelCalls;

        public Task Rebuilt => _rebuilt.Task;
        public int RebuildCalls => Volatile.Read(ref _rebuildCalls);
        public int CancelCalls => Volatile.Read(ref _cancelCalls);

        /// <summary>Rules each RebuildAsync cycle reports as (re)embedded. A value &gt;0 simulates written chunks.</summary>
        public int EmbeddedPerCycle { get; init; } = 1;

        public int TotalToEmbed => 0;
        public int EmbeddedSoFar => 0;
        public bool IsRebuilding => false;
        public string? CurrentRuleId => null;

        public Task<int> RebuildAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _rebuildCalls);
            _rebuilt.TrySetResult();
            return Task.FromResult(EmbeddedPerCycle);
        }

        public void CancelRebuild() => Interlocked.Increment(ref _cancelCalls);

        public Task EnsureVectorIndexAsync(CancellationToken ct) => Task.CompletedTask;

        public Task EmbedSingleAsync(string ruleId, string body, string? chunkStyle, CancellationToken ct)
            => Task.CompletedTask;

        public Task<bool> HasEmbeddingAsync(string ruleId, string bodyHash, CancellationToken ct)
            => Task.FromResult(false);

        public Task<(int Embedded, int Pending, int Failed, int Total)> GetCoverageAsync(CancellationToken ct)
            => Task.FromResult((0, 0, 0, 0));
    }
}
