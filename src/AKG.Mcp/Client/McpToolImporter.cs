using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Mcp.Client;

/// <summary>
/// Reads the <c>EXTERNAL_MCP_SERVERS</c> environment variable at startup
/// and imports all tools from each listed MCP server into the internal tool registry.
/// Also supports authenticated servers via <c>N8N_MCP_URL</c>/<c>N8N_MCP_TOKEN</c>.
/// Imported tools are wrapped as <see cref="McpToolSource"/> instances.
/// </summary>
public sealed class McpToolImporter : IMcpToolImporter
{
    private readonly IToolRegistry _toolRegistry;
    private readonly Func<string, IReadOnlyDictionary<string, string>?, IExternalMcpClient> _clientFactory;
    private readonly ILogger<McpToolImporter> _logger;

    /// <summary>
    /// Initializes a new <see cref="McpToolImporter"/>.
    /// </summary>
    /// <param name="toolRegistry">The registry to import discovered tools into.</param>
    /// <param name="clientFactory">Factory that creates an <see cref="IExternalMcpClient"/> for a given server URL with optional HTTP headers.</param>
    /// <param name="logger">Logger for import progress events.</param>
    public McpToolImporter(
        IToolRegistry toolRegistry,
        Func<string, IReadOnlyDictionary<string, string>?, IExternalMcpClient> clientFactory,
        ILogger<McpToolImporter> logger)
    {
        _toolRegistry = toolRegistry;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Imports all tools from a single external MCP server into the tool registry.
    /// </summary>
    /// <param name="serverUrl">The base URL of the external MCP server.</param>
    /// <param name="headers">Optional HTTP headers (e.g. Authorization) to include in requests.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ImportAsync(string serverUrl, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        _logger.LogInformation("Importing tools from external MCP server {Url}", serverUrl);

        var client = _clientFactory(serverUrl, headers);
        var tools = await client.ListToolsAsync(ct);

        foreach (var mcpTool in tools)
        {
            var toolSource = new McpToolSource(
                client,
                mcpTool,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<McpToolSource>.Instance);

            _toolRegistry.Register(toolSource);

            _logger.LogInformation(
                "Imported MCP tool '{Name}' from {Server}", mcpTool.Name, serverUrl);
        }
    }

    /// <summary>
    /// Reads <c>EXTERNAL_MCP_SERVERS</c> (comma-separated URLs) and imports tools from all listed servers.
    /// Also imports from <c>N8N_MCP_URL</c> if configured (with <c>N8N_MCP_TOKEN</c> for auth).
    /// Silently skips servers that are unreachable and logs the error.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task ImportAllFromEnvironmentAsync(CancellationToken ct)
    {
        var env = Environment.GetEnvironmentVariable("EXTERNAL_MCP_SERVERS");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var urls = env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var url in urls)
            {
                try
                {
                    await ImportAsync(url, headers: null, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to import tools from external MCP server {Url}", url);
                }
            }
        }

        await ImportN8nMcpAsync(ct);
    }

    /// <summary>
    /// Imports tools from the n8n MCP server if <c>N8N_MCP_URL</c> is configured.
    /// Resolves the Bearer token from <c>N8N_MCP_TOKEN</c> environment variable.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    private async Task ImportN8nMcpAsync(CancellationToken ct)
    {
        var n8nUrl = Environment.GetEnvironmentVariable("N8N_MCP_URL");
        if (string.IsNullOrWhiteSpace(n8nUrl))
            return;

        var token = Environment.GetEnvironmentVariable("N8N_MCP_TOKEN");
        Dictionary<string, string>? headers = null;

        if (!string.IsNullOrWhiteSpace(token))
        {
            headers = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}"
            };
        }

        try
        {
            await ImportAsync(n8nUrl, headers, ct);
            _logger.LogInformation("n8n MCP server imported from {Url}", n8nUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import tools from n8n MCP server {Url}", n8nUrl);
        }
    }
}
