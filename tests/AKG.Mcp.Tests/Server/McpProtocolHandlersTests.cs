using Edda.AKG.Mcp.Models;
using Edda.AKG.Mcp.Server;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Moq;
using System.Text.Json;

namespace Edda.AKG.Mcp.Tests.Server;

public class McpProtocolHandlersTests
{
    private static McpProtocolHandlers CreateSut(
        IReadOnlyList<ToolDefinition> tools,
        McpExposurePolicy policy,
        IToolExecutor? executor = null)
    {
        var internalRegistry = new Mock<IToolRegistry>();
        internalRegistry.Setup(r => r.GetAvailableTools()).Returns(tools);
        var mcpRegistry = new McpToolRegistry(internalRegistry.Object, () => policy);
        var server = new McpServer(
            mcpRegistry, executor ?? Mock.Of<IToolExecutor>(), NullLogger<McpServer>.Instance);
        return new McpProtocolHandlers(
            mcpRegistry, server, new HttpContextAccessor(), NullLogger<McpProtocolHandlers>.Instance);
    }

    private static ToolDefinition Def(string name, object? schema = null) =>
        new() { Name = name, Description = $"{name} desc", InputSchema = schema ?? new { type = "object" } };

    [Fact]
    public void BuildExposedTools_MapsAllowListedTools_ToSdkTools()
    {
        var sut = CreateSut([Def("search_memory")], new McpExposurePolicy(["search_memory"]));

        var tools = sut.BuildExposedTools();

        tools.Should().ContainSingle();
        tools[0].Name.Should().Be("search_memory");
        tools[0].Description.Should().Be("search_memory desc");
        tools[0].InputSchema.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void BuildExposedTools_OmitsNonAllowListedTools()
    {
        var sut = CreateSut(
            [Def("search_memory"), Def("manage_credentials")],
            new McpExposurePolicy(["search_memory"]));

        sut.BuildExposedTools().Select(t => t.Name).Should().Equal("search_memory");
    }

    [Fact]
    public void BuildExposedTools_InvalidSchema_FallsBackToObjectSchema()
    {
        // A non-object schema must not crash and must yield a minimal {"type":"object"} schema.
        var sut = CreateSut(
            [Def("search_memory", schema: "not-a-schema")],
            new McpExposurePolicy(["search_memory"]));

        var tool = sut.BuildExposedTools().Single();
        tool.InputSchema.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void ToCallToolResult_MapsContentAndError()
    {
        var mcp = new McpToolResult { Content = [new McpTextContent("hello")], IsError = true };

        var result = McpProtocolHandlers.ToCallToolResult(mcp);

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle();
        ((TextContentBlock)result.Content[0]).Text.Should().Be("hello");
    }

    [Fact]
    public async Task InvokeAsync_MissingName_ReturnsError()
    {
        var sut = CreateSut([], new McpExposurePolicy([]));

        var result = await sut.InvokeAsync(null, null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle(c => c.Text.Contains("Missing tool name"));
    }

    [Fact]
    public async Task InvokeAsync_NonAllowListedTool_ReturnsError()
    {
        var executor = new Mock<IToolExecutor>();
        var sut = CreateSut([], new McpExposurePolicy(["search_memory"]), executor.Object);

        var result = await sut.InvokeAsync("manage_credentials", null, CancellationToken.None);

        result.IsError.Should().BeTrue();
        executor.Verify(e => e.ExecuteAsync(
            It.IsAny<ToolCall>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_AllowListedTool_ConvertsArgumentsAndExecutes()
    {
        ToolCall? captured = null;
        var executor = new Mock<IToolExecutor>();
        executor
            .Setup(e => e.ExecuteAsync(
                It.IsAny<ToolCall>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .Callback<ToolCall, ToolExecutionContext, CancellationToken>((c, _, _) => captured = c)
            .ReturnsAsync(ToolResult.Ok("id-1", "search_memory", "ok"));

        var sut = CreateSut(
            [Def("search_memory")],
            new McpExposurePolicy(["search_memory"]),
            executor.Object);

        var args = new Dictionary<string, JsonElement>
        {
            ["task"] = JsonSerializer.SerializeToElement("hello"),
            ["k"] = JsonSerializer.SerializeToElement(5),
            ["flag"] = JsonSerializer.SerializeToElement(true)
        };

        var result = await sut.InvokeAsync("search_memory", args, CancellationToken.None);

        result.IsError.Should().BeFalse();
        captured.Should().NotBeNull();
        captured!.Arguments["task"].Should().Be("hello");
        captured.Arguments["k"].Should().Be(5L);
        captured.Arguments["flag"].Should().Be(true);
    }
}
