using Edda.AKG.Mcp.Models;
using Edda.AKG.Mcp.Server;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Mcp.Tests.Server;

public class McpServerTests
{
    private static McpServer CreateSut(
        IToolRegistry? registry = null,
        IToolExecutor? executor = null,
        McpExposurePolicy? policy = null)
    {
        var toolRegistry = new McpToolRegistry(
            registry ?? Mock.Of<IToolRegistry>(r =>
                r.GetAvailableTools() == new List<ToolDefinition>()),
            () => policy ?? new McpExposurePolicy(["tool_a", "tool_b"]));
        return new McpServer(
            toolRegistry,
            executor ?? Mock.Of<IToolExecutor>(),
            NullLogger<McpServer>.Instance);
    }

    [Fact]
    public void McpServer_ListTools_ExposesAllowListedTools()
    {
        var registryMock = new Mock<IToolRegistry>();
        registryMock.Setup(r => r.GetAvailableTools()).Returns(
        [
            new ToolDefinition { Name = "tool_a", Description = "Tool A", InputSchema = new { } },
            new ToolDefinition { Name = "tool_b", Description = "Tool B", InputSchema = new { } }
        ]);

        var sut = CreateSut(registry: registryMock.Object);

        var tools = sut.ListTools();

        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().Contain(["tool_a", "tool_b"]);
    }

    [Fact]
    public void McpServer_ListTools_OmitsNonAllowListedTools()
    {
        var registryMock = new Mock<IToolRegistry>();
        registryMock.Setup(r => r.GetAvailableTools()).Returns(
        [
            new ToolDefinition { Name = "tool_a", Description = "Tool A", InputSchema = new { } },
            new ToolDefinition { Name = "shell_execute", Description = "Dangerous", InputSchema = new { } }
        ]);

        // Policy allows only tool_a — the dangerous tool must be filtered out of tools/list.
        var sut = CreateSut(registry: registryMock.Object, policy: new McpExposurePolicy(["tool_a"]));

        var tools = sut.ListTools();

        tools.Should().ContainSingle(t => t.Name == "tool_a");
        tools.Select(t => t.Name).Should().NotContain("shell_execute");
    }

    [Fact]
    public async Task McpServer_CallTool_ExecutesViaToolExecutor()
    {
        var executorMock = new Mock<IToolExecutor>();
        executorMock
            .Setup(e => e.ExecuteAsync(
                It.Is<ToolCall>(c => c.Name == "tool_a"),
                It.IsAny<ToolExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Ok("call-1", "tool_a", "success output"));

        var sut = CreateSut(executor: executorMock.Object);
        var mcpCall = new McpToolCall { Id = "call-1", Name = "tool_a" };
        var ctx = new ToolExecutionContext { ConversationId = "conv-1" };

        var result = await sut.CallToolAsync(mcpCall, ctx);

        result.IsError.Should().BeFalse();
        result.Content.Should().ContainSingle(c => c.Text == "success output");
        executorMock.Verify(e => e.ExecuteAsync(
            It.Is<ToolCall>(c => c.Name == "tool_a"),
            ctx, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task McpServer_CallTool_ReturnsIsErrorOnToolFailure()
    {
        var executorMock = new Mock<IToolExecutor>();
        executorMock
            .Setup(e => e.ExecuteAsync(
                It.IsAny<ToolCall>(),
                It.IsAny<ToolExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolResult.Fail("call-1", "tool_a", "tool failed"));

        var sut = CreateSut(executor: executorMock.Object);

        var result = await sut.CallToolAsync(
            new McpToolCall { Name = "tool_a" },
            new ToolExecutionContext { ConversationId = "c1" });

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle(c => c.Text == "tool failed");
    }

    [Fact]
    public async Task McpServer_CallTool_RejectsNonAllowListedTool()
    {
        var executorMock = new Mock<IToolExecutor>();

        // "manage_credentials" is not in the allow-list → must be rejected before execution.
        var sut = CreateSut(
            executor: executorMock.Object,
            policy: new McpExposurePolicy(["tool_a"]));

        var result = await sut.CallToolAsync(
            new McpToolCall { Name = "manage_credentials" },
            new ToolExecutionContext { ConversationId = "c1" });

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle(c => c.Text.Contains("not available via MCP"));
        executorMock.Verify(e => e.ExecuteAsync(
            It.IsAny<ToolCall>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
