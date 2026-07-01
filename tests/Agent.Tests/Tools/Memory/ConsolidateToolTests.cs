using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class ConsolidateToolTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ConsolidateTool _sut;

    public ConsolidateToolTests()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero));
        _graph.Setup(g => g.DeleteRuleAsync(
                  It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        _sut = new ConsolidateTool(_graph.Object, _time, NullLogger<ConsolidateTool>.Instance);
    }

    private static ToolCall Call() =>
        new() { Id = "tc-1", Name = "consolidate_memory", Arguments = new Dictionary<string, object?>() };

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    private static KnowledgeRule Memory(string body, DateTimeOffset created, string owner = "user-1") =>
        MemoryNodes.Create(owner, body, created);

    private void SetupMemories(params KnowledgeRule[] rules) =>
        _graph.Setup(g => g.GetRulesAsync(null, "Memory", null, "user-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(rules);

    [Fact]
    public async Task ExecuteAsync_RemovesNormalizedDuplicates_KeepingNewest()
    {
        var older = Memory("Bob likes Tea", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = Memory("bob   likes tea", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        SetupMemories(older, newer);

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        _graph.Verify(g => g.DeleteRuleAsync(older.Id, "user-1", false, It.IsAny<CancellationToken>()), Times.Once);
        _graph.Verify(g => g.DeleteRuleAsync(newer.Id, "user-1", false, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PrunesFadedMemories()
    {
        _time.SetUtcNow(new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var stale = Memory("ancient fact", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SetupMemories(stale);

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        _graph.Verify(g => g.DeleteRuleAsync(stale.Id, "user-1", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_KeepsFreshUniqueMemories_DeletesNothing()
    {
        SetupMemories(
            Memory("unique one", new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero)),
            Memory("unique two", new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero)));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        _graph.Verify(g => g.DeleteRuleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoMemories_Succeeds()
    {
        SetupMemories();

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
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
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("consolidate_memory");
    }
}
