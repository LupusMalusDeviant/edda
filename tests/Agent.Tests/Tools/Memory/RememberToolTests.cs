using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class RememberToolTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RememberTool _sut;

    public RememberToolTests()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);
        _sut = new RememberTool(_graph.Object, _time, NullLogger<RememberTool>.Instance);
    }

    private static ToolCall Call(string? content = "Bob prefers dark mode")
    {
        var args = new Dictionary<string, object?>();
        if (content is not null) args["content"] = content;
        return new ToolCall { Id = "tc-1", Name = "remember", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    [Fact]
    public async Task ExecuteAsync_UpsertsMemoryNode_ScopedToUser()
    {
        var result = await _sut.ExecuteAsync(Call(), Ctx("alice"));

        result.Success.Should().BeTrue();
        _graph.Verify(g => g.UpsertRuleAsync(
            It.Is<KnowledgeRule>(r =>
                r.OwnerId == "alice" &&
                r.SourceType == "memory" &&
                r.Type == "Memory" &&
                r.Body == "Bob prefers dark mode"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SameContent_ProducesSameId_Idempotent()
    {
        KnowledgeRule? first = null;
        KnowledgeRule? second = null;
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
              .Callback<KnowledgeRule, CancellationToken>((r, _) =>
              {
                  if (first is null) first = r; else second = r;
              })
              .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);

        await _sut.ExecuteAsync(Call("same fact"), Ctx("bob"));
        await _sut.ExecuteAsync(Call("same fact"), Ctx("bob"));

        first!.Id.Should().Be(second!.Id);
    }

    [Fact]
    public async Task ExecuteAsync_MissingContent_ReturnsFail_WithoutUpsert()
    {
        var result = await _sut.ExecuteAsync(Call(content: null), Ctx());

        result.Success.Should().BeFalse();
        _graph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_BlankContent_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call("   "), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_GraphThrows_ReturnsFail()
    {
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("graph down"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("graph down");
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("remember");
    }
}
