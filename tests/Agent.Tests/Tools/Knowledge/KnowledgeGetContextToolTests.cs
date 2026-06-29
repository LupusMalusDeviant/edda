using Edda.Agent.Tools.Knowledge;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Knowledge;

public class KnowledgeGetContextToolTests
{
    private readonly Mock<IKnowledgeGraph> _kg = new();
    private readonly KnowledgeGetContextTool _sut;

    public KnowledgeGetContextToolTests()
    {
        _sut = new KnowledgeGetContextTool(_kg.Object, NullLogger<KnowledgeGetContextTool>.Instance);
    }

    private static ToolCall Call(string? query = "how to use async await")
    {
        var args = new Dictionary<string, object?>();
        if (query is not null) args["query"] = query;
        return new ToolCall { Id = "tc-1", Name = "search_memory", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    [Fact]
    public async Task ExecuteAsync_ValidQuery_ReturnsFormattedContext()
    {
        _kg.Setup(k => k.CompileContextAsync(It.IsAny<TaskContext>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new ContextResult { FormattedContext = "## Rules\n- Use async/await" });

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("async/await");
    }

    [Fact]
    public async Task ExecuteAsync_PassesUserIdToContext()
    {
        TaskContext? capturedCtx = null;
        _kg.Setup(k => k.CompileContextAsync(It.IsAny<TaskContext>(), It.IsAny<CancellationToken>()))
           .Callback<TaskContext, CancellationToken>((ctx, _) => capturedCtx = ctx)
           .ReturnsAsync(ContextResult.Empty);

        await _sut.ExecuteAsync(Call(), Ctx("alice"));

        capturedCtx.Should().NotBeNull();
        capturedCtx!.UserId.Should().Be("alice");
    }

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ReturnsFail()
    {
        var call = new ToolCall
        {
            Id = "tc-1", Name = "search_memory",
            Arguments = new Dictionary<string, object?>()
        };

        var result = await _sut.ExecuteAsync(call, Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_KgThrows_ReturnsFail()
    {
        _kg.Setup(k => k.CompileContextAsync(It.IsAny<TaskContext>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("AKG error"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("AKG error");
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("search_memory");
    }
}
