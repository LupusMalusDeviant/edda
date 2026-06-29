using Edda.AKG.Mcp.Client;
using Edda.AKG.Mcp.Models;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Mcp.Tests.Client;

public class McpToolSourceTests
{
    private static McpToolSource CreateSut(IExternalMcpClient client, string toolName = "remote_tool")
    {
        var definition = new McpToolDefinition
        {
            Name = toolName,
            Description = "A remote tool",
            InputSchema = new { }
        };
        return new McpToolSource(client, definition, NullLogger<McpToolSource>.Instance);
    }

    private static ToolCall Call(string name = "remote_tool") =>
        new() { Id = "c1", Name = name, Arguments = new Dictionary<string, object?>() };

    private static ToolExecutionContext Ctx() => new() { ConversationId = "conv-1" };

    [Fact]
    public async Task McpToolSource_Execute_ForwardsToExternalClient()
    {
        var clientMock = new Mock<IExternalMcpClient>();
        clientMock
            .Setup(c => c.CallToolAsync(
                "remote_tool",
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult
            {
                Content = [new McpTextContent("remote result")],
                IsError = false
            });

        var sut = CreateSut(clientMock.Object);

        var result = await sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Be("remote result");
        clientMock.Verify(c => c.CallToolAsync(
            "remote_tool",
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task McpToolSource_Execute_ReturnsFailOnIsError()
    {
        var clientMock = new Mock<IExternalMcpClient>();
        clientMock
            .Setup(c => c.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new McpToolResult
            {
                Content = [new McpTextContent("remote error")],
                IsError = true
            });

        var sut = CreateSut(clientMock.Object);

        var result = await sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("remote error");
    }

    [Fact]
    public async Task McpToolSource_Execute_ReturnsFailOnException()
    {
        var clientMock = new Mock<IExternalMcpClient>();
        clientMock
            .Setup(c => c.CallToolAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var sut = CreateSut(clientMock.Object);

        var result = await sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("connection refused");
    }

    [Fact]
    public void McpToolSource_Definition_MapsFromMcpToolDefinition()
    {
        var definition = new McpToolDefinition
        {
            Name = "test_tool",
            Description = "A test description",
            InputSchema = new { type = "object" }
        };
        var sut = new McpToolSource(Mock.Of<IExternalMcpClient>(), definition,
            NullLogger<McpToolSource>.Instance);

        sut.Definition.Name.Should().Be("test_tool");
        sut.Definition.Description.Should().Be("A test description");
    }
}
