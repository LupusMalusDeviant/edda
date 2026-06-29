using Edda.AKG.Mcp.Models;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Mcp.Client;

/// <summary>
/// Wraps an external MCP tool as an internal <see cref="IAgentTool"/>,
/// allowing the agent runtime to invoke external tools transparently.
/// </summary>
public sealed class McpToolSource : IAgentTool
{
    private readonly IExternalMcpClient _client;
    private readonly ILogger<McpToolSource> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; }

    /// <summary>
    /// Initializes a new <see cref="McpToolSource"/>.
    /// </summary>
    /// <param name="client">The external MCP client to forward calls to.</param>
    /// <param name="definition">The MCP tool definition describing this tool.</param>
    /// <param name="logger">Logger for diagnostic events.</param>
    public McpToolSource(
        IExternalMcpClient client,
        McpToolDefinition definition,
        ILogger<McpToolSource> logger)
    {
        _client = client;
        _logger = logger;
        Definition = new ToolDefinition
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = definition.InputSchema
        };
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mcpResult = await _client.CallToolAsync(
                call.Name, call.Arguments, cancellationToken);

            var text = mcpResult.Content.FirstOrDefault()?.Text ?? "";

            return mcpResult.IsError
                ? ToolResult.Fail(call.Id, call.Name, text)
                : ToolResult.Ok(call.Id, call.Name, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External MCP call to {Tool} failed", call.Name);
            return ToolResult.Fail(call.Id, call.Name, ex.Message);
        }
    }
}
