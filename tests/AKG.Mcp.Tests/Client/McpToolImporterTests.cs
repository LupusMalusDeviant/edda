using Edda.AKG.Mcp.Client;
using Edda.AKG.Mcp.Models;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Mcp.Tests.Client;

public class McpToolImporterTests
{
    private static (McpToolImporter Importer, Mock<IToolRegistry> Registry, Mock<IExternalMcpClient> Client)
        CreateSut(IReadOnlyList<McpToolDefinition>? tools = null)
    {
        var registry = new Mock<IToolRegistry>();
        var client = new Mock<IExternalMcpClient>();
        client.Setup(c => c.ListToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tools ?? []);

        IExternalMcpClient Factory(string _, IReadOnlyDictionary<string, string>? __) => client.Object;

        var importer = new McpToolImporter(
            registry.Object,
            Factory,
            NullLogger<McpToolImporter>.Instance);

        return (importer, registry, client);
    }

    [Fact]
    public async Task McpToolImporter_ExternalServer_RegistersAllTools()
    {
        var (importer, registry, _) = CreateSut(
        [
            new McpToolDefinition { Name = "tool_a", Description = "Tool A", InputSchema = new { } },
            new McpToolDefinition { Name = "tool_b", Description = "Tool B", InputSchema = new { } }
        ]);

        await importer.ImportAsync("http://mcp-server:3000", headers: null, CancellationToken.None);

        registry.Verify(r => r.Register(It.IsAny<IAgentTool>()), Times.Exactly(2));
    }

    [Fact]
    public async Task McpToolImporter_EmptyToolList_RegistersNothing()
    {
        var (importer, registry, _) = CreateSut([]);

        await importer.ImportAsync("http://mcp-server:3000", headers: null, CancellationToken.None);

        registry.Verify(r => r.Register(It.IsAny<IAgentTool>()), Times.Never);
    }

    [Fact]
    public async Task McpToolImporter_ImportAllFromEnvironment_SkipsWhenEnvVarEmpty()
    {
        var (importer, registry, _) = CreateSut();

        Environment.SetEnvironmentVariable("EXTERNAL_MCP_SERVERS", "");
        try
        {
            await importer.ImportAllFromEnvironmentAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EXTERNAL_MCP_SERVERS", null);
        }

        registry.Verify(r => r.Register(It.IsAny<IAgentTool>()), Times.Never);
    }

    [Fact]
    public async Task McpToolImporter_ImportAllFromEnvironment_ImportsFromMultipleServers()
    {
        var registry = new Mock<IToolRegistry>();
        var callCount = 0;
        IExternalMcpClient Factory(string _, IReadOnlyDictionary<string, string>? __)
        {
            callCount++;
            var client = new Mock<IExternalMcpClient>();
            client.Setup(c => c.ListToolsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([new McpToolDefinition { Name = $"tool_{callCount}", Description = "t", InputSchema = new { } }]);
            return client.Object;
        }

        var importer = new McpToolImporter(
            registry.Object,
            Factory,
            NullLogger<McpToolImporter>.Instance);

        Environment.SetEnvironmentVariable("EXTERNAL_MCP_SERVERS", "http://s1:3000,http://s2:3000");
        try
        {
            await importer.ImportAllFromEnvironmentAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("EXTERNAL_MCP_SERVERS", null);
        }

        registry.Verify(r => r.Register(It.IsAny<IAgentTool>()), Times.Exactly(2));
    }

    [Fact]
    public async Task McpToolImporter_ImportAllFromEnvironment_ImportsN8nMcpWithAuth()
    {
        var registry = new Mock<IToolRegistry>();
        IReadOnlyDictionary<string, string>? capturedHeaders = null;

        IExternalMcpClient Factory(string _, IReadOnlyDictionary<string, string>? headers)
        {
            capturedHeaders = headers;
            var client = new Mock<IExternalMcpClient>();
            client.Setup(c => c.ListToolsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([new McpToolDefinition { Name = "gitlab_push", Description = "Push files", InputSchema = new { } }]);
            return client.Object;
        }

        var importer = new McpToolImporter(
            registry.Object,
            Factory,
            NullLogger<McpToolImporter>.Instance);

        Environment.SetEnvironmentVariable("EXTERNAL_MCP_SERVERS", null);
        Environment.SetEnvironmentVariable("N8N_MCP_URL", "https://n8n.example.com/mcp/edda-gitlab");
        Environment.SetEnvironmentVariable("N8N_MCP_TOKEN", "test-bearer-token");
        try
        {
            await importer.ImportAllFromEnvironmentAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("N8N_MCP_URL", null);
            Environment.SetEnvironmentVariable("N8N_MCP_TOKEN", null);
        }

        registry.Verify(r => r.Register(It.IsAny<IAgentTool>()), Times.Once);
        capturedHeaders.Should().NotBeNull();
        capturedHeaders!["Authorization"].Should().Be("Bearer test-bearer-token");
    }

    [Fact]
    public async Task McpToolImporter_ImportAllFromEnvironment_SkipsN8nWhenUrlNotSet()
    {
        var (importer, registry, _) = CreateSut();

        Environment.SetEnvironmentVariable("EXTERNAL_MCP_SERVERS", null);
        Environment.SetEnvironmentVariable("N8N_MCP_URL", null);
        try
        {
            await importer.ImportAllFromEnvironmentAsync(CancellationToken.None);
        }
        finally
        {
            Environment.SetEnvironmentVariable("N8N_MCP_URL", null);
        }

        registry.Verify(r => r.Register(It.IsAny<IAgentTool>()), Times.Never);
    }
}
