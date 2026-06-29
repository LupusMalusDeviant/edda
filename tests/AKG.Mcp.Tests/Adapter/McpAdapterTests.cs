using Edda.AKG.Mcp.Adapter;
using Edda.AKG.Mcp.Models;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Mcp.Tests.Adapter;

public class McpAdapterTests
{
    [Fact]
    public void McpAdapter_ToMcpTool_MapsNameAndDescription()
    {
        var tool = new Mock<IAgentTool>();
        tool.Setup(t => t.Definition).Returns(new ToolDefinition
        {
            Name = "web_fetch",
            Description = "Fetches a URL and returns its content.",
            InputSchema = new { type = "object" }
        });

        var result = McpAdapter.ToMcpTool(tool.Object);

        result.Name.Should().Be("web_fetch");
        result.Description.Should().Be("Fetches a URL and returns its content.");
    }

    [Fact]
    public void McpAdapter_FromMcpCall_MapsArgumentsCorrectly()
    {
        var mcpCall = new McpToolCall
        {
            Id = "call-42",
            Name = "web_fetch",
            Arguments = new Dictionary<string, object?> { ["url"] = "https://example.com" }
        };

        var result = McpAdapter.FromMcpCall(mcpCall);

        result.Id.Should().Be("call-42");
        result.Name.Should().Be("web_fetch");
        result.Arguments["url"].Should().Be("https://example.com");
    }

    [Fact]
    public void McpAdapter_FromMcpCall_GeneratesIdWhenNull()
    {
        var mcpCall = new McpToolCall
        {
            Id = null,
            Name = "some_tool",
            Arguments = new Dictionary<string, object?>()
        };

        var result = McpAdapter.FromMcpCall(mcpCall);

        result.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void McpAdapter_ToMcpResult_SetsIsErrorOnFailure()
    {
        var failResult = ToolResult.Fail("call-1", "my_tool", "Something went wrong");

        var mcpResult = McpAdapter.ToMcpResult(failResult);

        mcpResult.IsError.Should().BeTrue();
        mcpResult.Content.Should().ContainSingle(c => c.Text == "Something went wrong");
    }

    [Fact]
    public void McpAdapter_ToMcpResult_SetsIsErrorFalseOnSuccess()
    {
        var okResult = ToolResult.Ok("call-2", "my_tool", "All good");

        var mcpResult = McpAdapter.ToMcpResult(okResult);

        mcpResult.IsError.Should().BeFalse();
        mcpResult.Content.Should().ContainSingle(c => c.Text == "All good");
    }
}
