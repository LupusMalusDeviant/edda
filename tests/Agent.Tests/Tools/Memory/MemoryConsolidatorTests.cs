using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

/// <summary>
/// Unit tests for <see cref="MemoryConsolidator"/>: normalized-duplicate removal (keeping newest), fade
/// pruning, and per-user enumeration for the all-users pass (C10).
/// </summary>
public class MemoryConsolidatorTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly FakeTimeProvider _time = new();
    private readonly MemoryConsolidator _sut;

    public MemoryConsolidatorTests()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero));
        _graph.Setup(g => g.DeleteRuleAsync(
                  It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        _sut = new MemoryConsolidator(_graph.Object, _time, NullLogger<MemoryConsolidator>.Instance);
    }

    private static KnowledgeRule Memory(string body, DateTimeOffset created, string owner = "user-1") =>
        MemoryNodes.Create(owner, body, created);

    private void SetupMemories(string userId, params KnowledgeRule[] rules) =>
        _graph.Setup(g => g.GetRulesAsync(null, "Memory", null, userId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(rules);

    [Fact]
    public async Task ConsolidateUserAsync_RemovesNormalizedDuplicates_KeepingNewest()
    {
        var older = Memory("Bob likes Tea", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = Memory("bob   likes tea", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        SetupMemories("user-1", older, newer);

        var result = await _sut.ConsolidateUserAsync("user-1");

        result.DuplicatesRemoved.Should().Be(1);
        _graph.Verify(g => g.DeleteRuleAsync(older.Id, "user-1", false, It.IsAny<CancellationToken>()), Times.Once);
        _graph.Verify(g => g.DeleteRuleAsync(newer.Id, "user-1", false, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateUserAsync_PrunesFadedMemories()
    {
        _time.SetUtcNow(new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var stale = Memory("ancient fact", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SetupMemories("user-1", stale);

        var result = await _sut.ConsolidateUserAsync("user-1");

        result.FadedRemoved.Should().Be(1);
        _graph.Verify(g => g.DeleteRuleAsync(stale.Id, "user-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConsolidateUserAsync_KeepsFreshUniqueMemories_DeletesNothing()
    {
        SetupMemories("user-1",
            Memory("unique one", new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero)),
            Memory("unique two", new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero)));

        var result = await _sut.ConsolidateUserAsync("user-1");

        result.TotalRemoved.Should().Be(0);
        _graph.Verify(g => g.DeleteRuleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConsolidateUserAsync_NoMemories_ReturnsZeroForOneUser()
    {
        SetupMemories("user-1");

        var result = await _sut.ConsolidateUserAsync("user-1");

        result.UsersProcessed.Should().Be(1);
        result.TotalRemoved.Should().Be(0);
    }

    [Fact]
    public async Task ConsolidateAllAsync_ConsolidatesEveryOwner_AndAggregates()
    {
        _graph.Setup(g => g.ListOwnersAsync("Memory", It.IsAny<CancellationToken>()))
              .ReturnsAsync(["user-1", "user-2"]);
        var older = Memory("Bob likes Tea", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), owner: "user-1");
        var newer = Memory("bob   likes tea", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), owner: "user-1");
        SetupMemories("user-1", older, newer);
        SetupMemories("user-2", Memory("solo fresh", new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero), owner: "user-2"));

        var result = await _sut.ConsolidateAllAsync();

        result.UsersProcessed.Should().Be(2);
        result.DuplicatesRemoved.Should().Be(1);
        result.FadedRemoved.Should().Be(0);
        _graph.Verify(g => g.ListOwnersAsync("Memory", It.IsAny<CancellationToken>()), Times.Once);
        _graph.Verify(g => g.DeleteRuleAsync(older.Id, "user-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
