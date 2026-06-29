using System.Net;
using System.Text;
using Edda.AKG.Mcp.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edda.AKG.Mcp.Tests.Client;

public class ExternalMcpClientTests
{
    private static ExternalMcpClient CreateSut(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeMcpHttpHandler(responseJson, status);
        var http = new HttpClient(handler);
        return new ExternalMcpClient(
            "http://mcp-server:3000",
            http,
            NullLogger<ExternalMcpClient>.Instance);
    }

    private static string ToolsListResponse(params (string Name, string Description)[] tools)
    {
        var toolsJson = string.Join(",", tools.Select(t =>
            "{\"name\":\"" + t.Name + "\",\"description\":\"" + t.Description + "\",\"inputSchema\":{}}"));
        return "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[" + toolsJson + "]}}";
    }

    private static string CallToolResponse(string text, bool isError = false)
    {
        var isErrorStr = isError ? "true" : "false";
        return "{\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}],\"isError\":" + isErrorStr + "}}";
    }

    [Fact]
    public async Task ExternalMcpClient_ListTools_ReturnsDefinitions()
    {
        var sut = CreateSut(ToolsListResponse(("search_web", "Searches the web"), ("read_file", "Reads a file")));

        var tools = await sut.ListToolsAsync(CancellationToken.None);

        tools.Should().HaveCount(2);
        tools[0].Name.Should().Be("search_web");
        tools[0].Description.Should().Be("Searches the web");
        tools[1].Name.Should().Be("read_file");
    }

    [Fact]
    public async Task ExternalMcpClient_ListTools_EmptyList_ReturnsEmptyCollection()
    {
        var sut = CreateSut("""{"jsonrpc":"2.0","id":1,"result":{"tools":[]}}""");

        var tools = await sut.ListToolsAsync(CancellationToken.None);

        tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalMcpClient_CallTool_ForwardsToRemoteServer()
    {
        var sut = CreateSut(CallToolResponse("the answer"));

        var result = await sut.CallToolAsync(
            "search_web",
            new Dictionary<string, object?> { ["query"] = "dotnet" },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Content.Should().ContainSingle(c => c.Text == "the answer");
    }

    [Fact]
    public async Task ExternalMcpClient_CallTool_PropagatesIsErrorFromRemote()
    {
        var sut = CreateSut(CallToolResponse("tool crashed", isError: true));

        var result = await sut.CallToolAsync(
            "bad_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.Content.Should().ContainSingle(c => c.Text == "tool crashed");
    }

    [Fact]
    public async Task ExternalMcpClient_CallTool_ThrowsOnRpcError()
    {
        var rpcError = """{"jsonrpc":"2.0","id":2,"error":{"code":-32600,"message":"Method not found"}}""";
        var sut = CreateSut(rpcError);

        var act = async () => await sut.CallToolAsync(
            "unknown_tool",
            new Dictionary<string, object?>(),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Method not found*");
    }

    [Fact]
    public async Task ExternalMcpClient_ListTools_ThrowsOnHttpError()
    {
        var sut = CreateSut("{}", HttpStatusCode.InternalServerError);

        var act = async () => await sut.ListToolsAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ExternalMcpClient_ForwardsMcpSessionId_OnSubsequentRequests()
    {
        var sessionId = "test-session-abc";
        var handler = new FakeMcpHttpHandler(
            ToolsListResponse(("search_web", "Searches the web")),
            sessionId: sessionId);
        var http = new HttpClient(handler);
        var sut = new ExternalMcpClient(
            "http://mcp-server:3000", http,
            NullLogger<ExternalMcpClient>.Instance);

        await sut.ListToolsAsync(CancellationToken.None);

        // Request 0 = initialize, Request 1 = tools/list
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Headers.Contains("mcp-session-id").Should().BeFalse(
            "initialize request should not include session ID");
        handler.Requests[1].Headers.GetValues("mcp-session-id").Should().ContainSingle(sessionId,
            "subsequent requests must forward the session ID from initialize");
    }
}
