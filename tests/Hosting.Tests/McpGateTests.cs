using System.Net;
using Edda.Hosting.Tests.Infrastructure;

namespace Edda.Hosting.Tests;

/// <summary>Integration tests for the MCP-over-HTTP token gate (opt-in via MCP_SERVER_ENABLED).</summary>
public sealed class McpGateTests
{
    [Fact]
    public async Task Mcp_Disabled_HasNoMcpEndpoint()
    {
        // MCP_SERVER_ENABLED unset → neither MapMcp nor the token gate is added, so POST /mcp is not an MCP
        // endpoint: it is rejected by routing (404, or 405 when the Blazor fallback claims the path for GET) —
        // crucially NOT the gate's 401. The enabled case below proves the 401 gate is opt-in.
        using var factory = new HostingTestFactory(("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.PostAsync("/mcp", content: null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Mcp_Enabled_NoToken_ReturnsUnauthorized()
    {
        using var factory = new HostingTestFactory(("MCP_SERVER_ENABLED", "true"), ("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        var response = await client.PostAsync("/mcp", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MCP token required");
    }

    [Fact]
    public async Task Mcp_Enabled_LegacyQueryToken_StillUnauthorized()
    {
        using var factory = new HostingTestFactory(("MCP_SERVER_ENABLED", "true"), ("EDDA_AUTH_TOKEN", null));
        var client = factory.CreateClient();

        // A6: the '?token=' query parameter is ignored; only the Authorization header authenticates.
        var response = await client.PostAsync("/mcp?token=whatever", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
