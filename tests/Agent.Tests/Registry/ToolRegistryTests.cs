using Edda.Agent.Registry;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.OutputFilter;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Registry;

public class ToolRegistryTests
{
    private readonly Mock<IAuditLog> _auditLog = new();
    private readonly SecretRedactor _redactor = new();
    private readonly ToolRegistry _sut;

    public ToolRegistryTests()
    {
        _auditLog
            .Setup(a => a.LogAsync(It.IsAny<AuditEvent>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _sut = new ToolRegistry(_redactor, _auditLog.Object, NullLogger<ToolRegistry>.Instance);
    }

    private static IAgentTool MakeIAgentTool(string name, ToolResult result)
    {
        var mock = new Mock<IAgentTool>();
        var def = new ToolDefinition { Name = name, Description = "desc", InputSchema = new { } };
        mock.SetupGet(t => t.Definition).Returns(def);
        mock.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ToolCall MakeCall(string name = "test_tool") => new()
    {
        Id = "tc-1",
        Name = name,
        Arguments = new Dictionary<string, object?>()
    };

    private static ToolExecutionContext MakeCtx() => new()
    {
        ConversationId = "conv-1",
        UserId = "user-1"
    };

    [Fact]
    public void Register_IAgentTool_AppearInGetAvailableTools()
    {
        var tool = MakeIAgentTool("search_tool", ToolResult.Ok("tc-1", "search_tool", "results"));
        _sut.Register(tool);

        var available = _sut.GetAvailableTools();

        available.Should().ContainSingle(t => t.Name == "search_tool");
    }

    [Fact]
    public void Register_LambdaTool_AppearInGetAvailableTools()
    {
        var def = new ToolDefinition { Name = "lambda_tool", Description = "desc", InputSchema = new { } };
        _sut.Register(def, (_, _, _) => Task.FromResult(ToolResult.Ok("tc-1", "lambda_tool", "ok")));

        var available = _sut.GetAvailableTools();

        available.Should().ContainSingle(t => t.Name == "lambda_tool");
    }

    [Fact]
    public async Task ExecuteAsync_KnownTool_ReturnsSuccess()
    {
        _sut.Register(MakeIAgentTool("test_tool", ToolResult.Ok("tc-1", "test_tool", "output")));

        var result = await _sut.ExecuteAsync(MakeCall("test_tool"), MakeCtx());

        result.Success.Should().BeTrue();
        result.Content.Should().Be("output");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsFailWithMessage()
    {
        var result = await _sut.ExecuteAsync(MakeCall("nonexistent_tool"), MakeCtx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nonexistent_tool");
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrows_ReturnsFailWithExceptionMessage()
    {
        var mock = new Mock<IAgentTool>();
        var def = new ToolDefinition { Name = "throwing_tool", Description = "desc", InputSchema = new { } };
        mock.SetupGet(t => t.Definition).Returns(def);
        mock.Setup(t => t.ExecuteAsync(It.IsAny<ToolCall>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("kaboom"));
        _sut.Register(mock.Object);

        var result = await _sut.ExecuteAsync(MakeCall("throwing_tool"), MakeCtx());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("kaboom");
    }

    [Fact]
    public async Task ExecuteAsync_SecretInContent_ContentIsRedacted()
    {
        // sk-ant- followed by 20+ chars is redacted by SecretRedactor
        var secretContent = "Result: sk-ant-aaaabbbbccccddddeeee and more text";
        _sut.Register(MakeIAgentTool("secret_tool", ToolResult.Ok("tc-1", "secret_tool", secretContent)));

        var result = await _sut.ExecuteAsync(MakeCall("secret_tool"), MakeCtx());

        result.Success.Should().BeTrue();
        result.Content.Should().NotContain("sk-ant-aaaa");
        result.Content.Should().Contain("[API_KEY_ANT]");
    }

    [Fact]
    public async Task ExecuteAsync_Success_WritesToolExecuteAuditEntry()
    {
        _sut.Register(MakeIAgentTool("test_tool", ToolResult.Ok("tc-1", "test_tool", "ok")));

        await _sut.ExecuteAsync(MakeCall("test_tool"), MakeCtx());

        _auditLog.Verify(a => a.LogAsync(
            AuditEvent.ToolExecute,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Failure_WritesToolErrorAuditEntry()
    {
        _sut.Register(MakeIAgentTool("test_tool", ToolResult.Fail("tc-1", "test_tool", "fail")));

        await _sut.ExecuteAsync(MakeCall("test_tool"), MakeCtx());

        _auditLog.Verify(a => a.LogAsync(
            AuditEvent.ToolError,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_WritesToolErrorAuditEntry()
    {
        await _sut.ExecuteAsync(MakeCall("missing_tool"), MakeCtx());

        _auditLog.Verify(a => a.LogAsync(
            AuditEvent.ToolError,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteManyAsync_MultipleCalls_AllExecuted()
    {
        _sut.Register(MakeIAgentTool("tool_a", ToolResult.Ok("tc-1", "tool_a", "a")));
        _sut.Register(MakeIAgentTool("tool_b", ToolResult.Ok("tc-2", "tool_b", "b")));

        var calls = new[]
        {
            new ToolCall { Id = "tc-1", Name = "tool_a", Arguments = new Dictionary<string, object?>() },
            new ToolCall { Id = "tc-2", Name = "tool_b", Arguments = new Dictionary<string, object?>() }
        };

        var results = await _sut.ExecuteManyAsync(calls, MakeCtx());

        results.Should().HaveCount(2);
        results.All(r => r.Success).Should().BeTrue();
    }

    [Fact]
    public void GetFilteredTools_ReturnsOnlyNamedTools()
    {
        _sut.Register(MakeIAgentTool("alpha", ToolResult.Ok("x", "alpha", "ok")));
        _sut.Register(MakeIAgentTool("beta", ToolResult.Ok("x", "beta", "ok")));
        _sut.Register(MakeIAgentTool("gamma", ToolResult.Ok("x", "gamma", "ok")));

        var filtered = _sut.GetFilteredTools(["alpha", "gamma"]);

        filtered.Should().HaveCount(2);
        filtered.Select(t => t.Name).Should().BeEquivalentTo(["alpha", "gamma"]);
    }

    [Fact]
    public void GetTool_RegisteredTool_ReturnsInstance()
    {
        var tool = MakeIAgentTool("findable", ToolResult.Ok("x", "findable", "ok"));
        _sut.Register(tool);

        var found = _sut.GetTool("findable");

        found.Should().NotBeNull();
        found!.Definition.Name.Should().Be("findable");
    }

    [Fact]
    public void GetTool_UnknownTool_ReturnsNull()
    {
        var found = _sut.GetTool("does_not_exist");

        found.Should().BeNull();
    }

    [Fact]
    public void GetTool_LambdaTool_ReturnsNull()
    {
        // Lambda-registered tools have no IAgentTool instance
        var def = new ToolDefinition { Name = "lambda_only", Description = "desc", InputSchema = new { } };
        _sut.Register(def, (_, _, _) => Task.FromResult(ToolResult.Ok("tc", "lambda_only", "ok")));

        var found = _sut.GetTool("lambda_only");

        found.Should().BeNull();
    }

    // -- Unregister --------------------------------------------------------------------

    [Fact]
    public void Unregister_RegisteredTool_DisappearsFromGetAvailableTools()
    {
        _sut.Register(MakeIAgentTool("removable", ToolResult.Ok("tc-1", "removable", "ok")));

        _sut.Unregister("removable");

        _sut.GetAvailableTools().Should().NotContain(t => t.Name == "removable");
    }

    [Fact]
    public void Unregister_RegisteredTool_ReturnsNullFromGetTool()
    {
        _sut.Register(MakeIAgentTool("removable2", ToolResult.Ok("tc-1", "removable2", "ok")));

        _sut.Unregister("removable2");

        _sut.GetTool("removable2").Should().BeNull();
    }

    [Fact]
    public void Unregister_UnknownName_IsNoOp()
    {
        var act = () => _sut.Unregister("does_not_exist");

        act.Should().NotThrow();
        _sut.GetAvailableTools().Should().BeEmpty();
    }

    [Fact]
    public async Task Unregister_AfterUnregister_ToolIsNotExecutable()
    {
        _sut.Register(MakeIAgentTool("gone_tool", ToolResult.Ok("tc-1", "gone_tool", "ok")));
        _sut.Unregister("gone_tool");

        var result = await _sut.ExecuteAsync(MakeCall("gone_tool"), MakeCtx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("gone_tool");
    }

    [Fact]
    public void Unregister_OnlyRemovesSpecifiedTool_OtherToolsRemain()
    {
        _sut.Register(MakeIAgentTool("keep_me", ToolResult.Ok("tc-1", "keep_me", "ok")));
        _sut.Register(MakeIAgentTool("remove_me", ToolResult.Ok("tc-2", "remove_me", "ok")));

        _sut.Unregister("remove_me");

        _sut.GetAvailableTools().Should().ContainSingle(t => t.Name == "keep_me");
        _sut.GetAvailableTools().Should().NotContain(t => t.Name == "remove_me");
    }
}
