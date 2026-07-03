using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class ForgetToolTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly ForgetTool _sut;

    public ForgetToolTests()
    {
        _graph.Setup(g => g.DeleteRuleAsync(
                  It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        _sut = new ForgetTool(_graph.Object, NullLogger<ForgetTool>.Instance);
    }

    private static ToolCall Call(string? content = "Bob prefers dark mode")
    {
        var args = new Dictionary<string, object?>();
        if (content is not null) args["content"] = content;
        return new ToolCall { Id = "tc-1", Name = "forget", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    [Fact]
    public async Task ExecuteAsync_ExistingMemory_DeletesIt()
    {
        var id = MemoryNodes.NodeId("alice", "Bob prefers dark mode");
        _graph.Setup(g => g.GetRuleAsync(id, "alice", It.IsAny<CancellationToken>()))
              .ReturnsAsync(MemoryNodes.Create("alice", "Bob prefers dark mode", DateTimeOffset.UnixEpoch));

        var result = await _sut.ExecuteAsync(Call(), Ctx("alice"));

        result.Success.Should().BeTrue();
        _graph.Verify(g => g.DeleteRuleAsync(id, "alice", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownMemory_IsNoOp()
    {
        _graph.Setup(g => g.GetRuleAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((KnowledgeRule?)null);

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("No matching memory");
        _graph.Verify(g => g.DeleteRuleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MissingContent_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call(content: null), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_GraphThrows_ReturnsFail()
    {
        _graph.Setup(g => g.GetRuleAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("graph down"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("forget");
    }

    [Fact]
    public async Task ExecuteAsync_ViewerRole_ReturnsInsufficientRoleFail()
    {
        var authorizer = new Mock<IRuleAuthorizer>();
        authorizer.Setup(a => a.CanMutateOwn()).Returns(false);
        var sut = new ForgetTool(_graph.Object, NullLogger<ForgetTool>.Instance, authorizer.Object);

        var result = await sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Insufficient role");
        _graph.Verify(g => g.DeleteRuleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
