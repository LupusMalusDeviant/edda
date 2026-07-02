using Edda.AKG.Context;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Context;

/// <summary>
/// Unit tests for <see cref="SemanticBooster"/>: the vector-index retrieval path, the app-side
/// cosine fallback (incl. similarity threshold), the normalized keyword+semantic combination,
/// and graceful no-op behaviour when embeddings are unavailable or yield no matches.
/// </summary>
public class SemanticBoosterTests
{
    private static Mock<IEmbeddingService> AvailableEmbeddings(params float[] queryVector)
    {
        var emb = new Mock<IEmbeddingService>();
        emb.SetupGet(e => e.IsAvailable).Returns(true);
        emb.SetupGet(e => e.Dimensions).Returns(queryVector.Length);
        emb.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(queryVector);
        return emb;
    }

    private static ScoredRule Scored(string id, double score)
        => new()
        {
            Rule = new KnowledgeRule
            {
                Id = id, Type = "Rule", Domain = "general",
                Priority = RulePriority.Medium, Body = "body",
            },
            Score = score,
        };

    private static SemanticBooster Booster(IEmbeddingService embeddings, ICypherExecutor cypher)
        => new(embeddings, cypher, NullLogger<SemanticBooster>.Instance);

    private static TaskContext Ctx() => new() { Task = "some task", UserId = "u1" };

    [Fact]
    public async Task BoostAsync_EmbeddingUnavailable_ReturnsUnchangedWithoutEmbedding()
    {
        var emb = new Mock<IEmbeddingService>();
        emb.SetupGet(e => e.IsAvailable).Returns(false);
        var sut = Booster(emb.Object, new FakeCypherExecutor());
        var input = new[] { Scored("a", 10), Scored("b", 5) };

        var result = await sut.BoostAsync(input, Ctx(), CancellationToken.None);

        result.Select(r => r.Rule.Id).Should().Equal("a", "b");
        emb.Verify(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BoostAsync_VectorIndex_RanksBySemanticSimilarity()
    {
        // Equal keyword scores; the vector index makes rule-b far more similar than rule-a.
        var cypher = new FakeCypherExecutor();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> indexRows =
        [
            new Dictionary<string, object?> { ["id"] = "rule-a", ["score"] = 0.2 },
            new Dictionary<string, object?> { ["id"] = "rule-b", ["score"] = 0.9 },
        ];
        cypher.AddQueryHandler(q => q.Contains("queryNodes") ? indexRows : cypher.DefaultResult);

        var sut = Booster(AvailableEmbeddings(1f, 0f, 0f).Object, cypher);
        var input = new[] { Scored("rule-a", 10), Scored("rule-b", 10) };

        var result = await sut.BoostAsync(input, Ctx(), CancellationToken.None);

        result[0].Rule.Id.Should().Be("rule-b", because: "higher cosine similarity wins at equal keyword score");
        result.Select(r => r.Rule.Id).Should().Contain("rule-a");
    }

    [Fact]
    public async Task BoostAsync_IndexUnavailable_FallsBackToAppSideCosineWithThreshold()
    {
        var cypher = new FakeCypherExecutor();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> chunkRows =
        [
            // orthogonal to the query → cosine 0 (< 0.3 threshold) → no semantic contribution
            new Dictionary<string, object?>
            {
                ["id"] = "rule-a", ["embs"] = new List<object> { new List<object> { 0f, 1f, 0f } },
            },
            // identical to the query → cosine 1 → strong semantic contribution
            new Dictionary<string, object?>
            {
                ["id"] = "rule-b", ["embs"] = new List<object> { new List<object> { 1f, 0f, 0f } },
            },
        ];
        cypher.AddQueryHandler(q =>
        {
            if (q.Contains("queryNodes")) throw new InvalidOperationException("no vector index");
            if (q.Contains("collect(c.embedding) AS embs")) return chunkRows;
            return cypher.DefaultResult;
        });

        var sut = Booster(AvailableEmbeddings(1f, 0f, 0f).Object, cypher);
        var input = new[] { Scored("rule-a", 10), Scored("rule-b", 10) };

        var result = await sut.BoostAsync(input, Ctx(), CancellationToken.None);

        result[0].Rule.Id.Should().Be("rule-b");
        cypher.ExecutedQueries.Should().Contain(q => q.Contains("collect(c.embedding) AS embs"),
            because: "the app-side cosine fallback must run when the vector index is unavailable");
    }

    [Fact]
    public async Task BoostAsync_NoSemanticMatches_KeepsKeywordScores()
    {
        // No query handler → index query returns empty (index exists, nothing above threshold).
        var cypher = new FakeCypherExecutor();
        var sut = Booster(AvailableEmbeddings(1f, 0f, 0f).Object, cypher);
        var input = new[] { Scored("a", 10), Scored("b", 5) };

        var result = await sut.BoostAsync(input, Ctx(), CancellationToken.None);

        result.Select(r => r.Rule.Id).Should().Equal("a", "b");
        result.Single(r => r.Rule.Id == "a").Score.Should().Be(10);
        result.Single(r => r.Rule.Id == "b").Score.Should().Be(5);
    }

    // ── B6: app-side fallback is capped to the top keyword-ranked candidates and warns on activation ──

    [Fact]
    public void SelectFallbackCandidates_OverCap_ReturnsTopCandidatesInOrder()
    {
        IReadOnlyList<string> ids = ["a", "b", "c", "d", "e"];

        var result = SemanticBooster.SelectFallbackCandidates(ids, max: 3);

        result.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void SelectFallbackCandidates_WithinCap_ReturnsAllUnchanged()
    {
        IReadOnlyList<string> ids = ["a", "b"];

        var result = SemanticBooster.SelectFallbackCandidates(ids, max: 5);

        result.Should().BeSameAs(ids);
    }

    [Fact]
    public void SelectFallbackCandidates_ExactlyCap_ReturnsAll()
    {
        IReadOnlyList<string> ids = ["a", "b", "c"];

        var result = SemanticBooster.SelectFallbackCandidates(ids, max: 3);

        result.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task BoostAsync_IndexUnavailable_LogsFallbackWarning()
    {
        var cypher = new FakeCypherExecutor();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> chunkRows =
        [
            new Dictionary<string, object?>
            {
                ["id"] = "rule-a", ["embs"] = new List<object> { new List<object> { 1f, 0f, 0f } },
            },
        ];
        cypher.AddQueryHandler(q =>
        {
            if (q.Contains("queryNodes")) throw new InvalidOperationException("no vector index");
            if (q.Contains("collect(c.embedding) AS embs")) return chunkRows;
            return cypher.DefaultResult;
        });

        var logger = new CapturingLogger<SemanticBooster>();
        var sut = new SemanticBooster(AvailableEmbeddings(1f, 0f, 0f).Object, cypher, logger);

        await sut.BoostAsync([Scored("rule-a", 10)], Ctx(), CancellationToken.None);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("App-side cosine fallback active"));
    }

    /// <summary>Minimal <see cref="ILogger{T}"/> that records emitted level + formatted message.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
