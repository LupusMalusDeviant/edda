using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class RecallToolTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RecallTool _sut;

    public RecallToolTests()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        _sut = new RecallTool(_graph.Object, _time, NullLogger<RecallTool>.Instance);
    }

    private static ToolCall Call(string? query = "dark mode")
    {
        var args = new Dictionary<string, object?>();
        if (query is not null) args["query"] = query;
        return new ToolCall { Id = "tc-1", Name = "recall", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    private static KnowledgeRule Memory(string body, string owner = "user-1") =>
        MemoryNodes.Create(owner, body, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

    private static KnowledgeRule MemoryOn(string body, DateTimeOffset created, string owner = "user-1") =>
        MemoryNodes.Create(owner, body, created);

    private void SetupMemories(params KnowledgeRule[] rules) =>
        _graph.Setup(g => g.GetRulesAsync(null, "Memory", null, "user-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(rules);

    [Fact]
    public async Task ExecuteAsync_NoMemories_ReturnsFriendlyMessage()
    {
        SetupMemories();

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("No memories");
    }

    [Fact]
    public async Task ExecuteAsync_RanksByKeywordOverlap()
    {
        SetupMemories(
            Memory("Bob prefers dark mode in the editor"),
            Memory("Alice drinks coffee"));

        var result = await _sut.ExecuteAsync(Call("dark mode"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("dark mode");
        result.Content!.IndexOf("dark mode", StringComparison.Ordinal)
            .Should().BeLessThan(result.Content!.IndexOf("coffee", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FiltersToOwningUser()
    {
        _graph.Setup(g => g.GetRulesAsync(null, "Memory", null, "user-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync([Memory("mine fact", "user-1"), Memory("foreign fact", "other")]);

        var result = await _sut.ExecuteAsync(Call("mine foreign"), Ctx());

        result.Content.Should().Contain("mine fact");
        result.Content.Should().NotContain("foreign fact");
    }

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call(query: null), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_GraphThrows_ReturnsFail()
    {
        _graph.Setup(g => g.GetRulesAsync(
                  It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("graph down"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DecaysStaleMemories_RecentRanksHigher()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        _graph.Setup(g => g.GetRulesAsync(null, "Memory", null, "user-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(
              [
                  MemoryOn("project uses redis cache", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                  MemoryOn("project uses redis now", new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero)),
              ]);

        var result = await _sut.ExecuteAsync(Call("redis project"), Ctx());

        // Both match the query equally; the recent memory must rank above the stale one (forgetting curve).
        result.Content!.IndexOf("redis now", StringComparison.Ordinal)
            .Should().BeLessThan(result.Content!.IndexOf("redis cache", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_PrefersNewerOfTwoSimilarFacts_SupersededRanksLower()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var stale = MemoryOn("my favorite color is blue", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var current = MemoryOn("my favorite color is red", new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero))
            with { RelatesTo = new RuleRelations { Supersedes = [stale.Id] } };
        SetupMemories(stale, current);

        var result = await _sut.ExecuteAsync(Call("favorite color"), Ctx());

        // The current fact supersedes the older one, which is demoted below its replacement.
        result.Content!.IndexOf("red", StringComparison.Ordinal)
            .Should().BeLessThan(result.Content!.IndexOf("blue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_DisjointMemoryNotSuperseded_KeepsFullWeight()
    {
        // All three match the query equally with equal recency; only the superseded 'dark' fact is
        // penalised, so the un-superseded 'custom' fact must still outrank it.
        var stale = Memory("the theme setting is dark");
        var superseding = Memory("the theme setting is light")
            with { RelatesTo = new RuleRelations { Supersedes = [stale.Id] } };
        var disjoint = Memory("the theme setting is custom");
        SetupMemories(stale, superseding, disjoint);

        var result = await _sut.ExecuteAsync(Call("theme setting"), Ctx());

        result.Content!.IndexOf("custom", StringComparison.Ordinal)
            .Should().BeLessThan(result.Content!.IndexOf("is dark", StringComparison.Ordinal));
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("recall");
    }
}
