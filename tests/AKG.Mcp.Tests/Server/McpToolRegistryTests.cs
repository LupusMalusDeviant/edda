using Edda.AKG.Mcp.Server;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Mcp.Tests.Server;

public class McpToolRegistryTests
{
    private static McpToolRegistry CreateSut(
        IReadOnlyList<ToolDefinition> tools, McpExposurePolicy policy)
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetAvailableTools()).Returns(tools);
        return new McpToolRegistry(registry.Object, () => policy);
    }

    [Fact]
    public void GetMcpTools_IncludesAllowListedTools()
    {
        var sut = CreateSut(
            [new ToolDefinition { Name = "search_memory", Description = "ctx", InputSchema = new { } }],
            new McpExposurePolicy(["search_memory"]));

        var tools = sut.GetMcpTools();

        tools.Should().ContainSingle(t => t.Name == "search_memory");
    }

    [Fact]
    public void GetMcpTools_OmitsNonAllowListedTools()
    {
        var sut = CreateSut(
        [
            new ToolDefinition { Name = "search_memory", Description = "ctx", InputSchema = new { } },
            new ToolDefinition { Name = "manage_credentials", Description = "danger", InputSchema = new { } }
        ],
            new McpExposurePolicy(["search_memory"]));

        var tools = sut.GetMcpTools();

        tools.Should().ContainSingle(t => t.Name == "search_memory");
        tools.Select(t => t.Name).Should().NotContain("manage_credentials");
    }

    [Fact]
    public void IsExposed_DelegatesToPolicy()
    {
        var sut = CreateSut([], new McpExposurePolicy(["search_memory"]));

        sut.IsExposed("search_memory").Should().BeTrue();
        sut.IsExposed("manage_credentials").Should().BeFalse();
    }
}
