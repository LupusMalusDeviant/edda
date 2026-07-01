using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class RecallToolTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly RecallTool _sut;

    public RecallToolTests()
    {
        _sut = new RecallTool(_graph.Object, NullLogger<RecallTool>.Instance);
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
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("recall");
    }
}
