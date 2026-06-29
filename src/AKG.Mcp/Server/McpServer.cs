using Edda.AKG.Mcp.Adapter;
using Edda.AKG.Mcp.Models;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Mcp.Server;

/// <summary>
/// MCP protocol handler that exposes internal agent tools to external MCP clients.
/// Handles <c>tools/list</c> and <c>tools/call</c> protocol operations.
/// </summary>
public sealed class McpServer : IMcpServer
{
    private readonly IMcpToolRegistry _toolRegistry;
    private readonly IToolExecutor _toolExecutor;
    private readonly ILogger<McpServer> _logger;

    /// <summary>
    /// Initializes a new <see cref="McpServer"/>.
    /// </summary>
    /// <param name="toolRegistry">Registry that lists internal tools as MCP definitions.</param>
    /// <param name="toolExecutor">Executor for dispatching tool calls.</param>
    /// <param name="logger">Logger for protocol events.</param>
    public McpServer(
        IMcpToolRegistry toolRegistry,
        IToolExecutor toolExecutor,
        ILogger<McpServer> logger)
    {
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _logger = logger;
    }

    /// <summary>
    /// Returns all internally registered tools as MCP tool definitions.
    /// Corresponds to the MCP <c>tools/list</c> protocol method.
    /// </summary>
    /// <returns>All available tools as MCP definitions.</returns>
    public IReadOnlyList<McpToolDefinition> ListTools()
    {
        var tools = _toolRegistry.GetMcpTools();
        _logger.LogInformation("MCP tools/list returned {Count} tools", tools.Count);
        return tools;
    }

    /// <summary>
    /// Executes a tool invocation received from an external MCP client.
    /// Corresponds to the MCP <c>tools/call</c> protocol method.
    /// </summary>
    /// <param name="mcpCall">The MCP tool call to execute.</param>
    /// <param name="context">Execution context for the tool call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The MCP tool result including content and error status.</returns>
    public async Task<McpToolResult> CallToolAsync(
        McpToolCall mcpCall,
        ToolExecutionContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "MCP tools/call: {Tool} (id={Id})", mcpCall.Name, mcpCall.Id);

        // Defense-in-depth: reject tools that are not allow-listed for MCP exposure,
        // even if a client crafts a call for a tool that was never advertised by tools/list.
        if (!_toolRegistry.IsExposed(mcpCall.Name))
        {
            _logger.LogWarning(
                "MCP tools/call rejected: tool '{Tool}' is not exposed via MCP", mcpCall.Name);
            return new McpToolResult
            {
                Content = [new McpTextContent($"Tool '{mcpCall.Name}' is not available via MCP.")],
                IsError = true
            };
        }

        var toolCall = McpAdapter.FromMcpCall(mcpCall);
        var result = await _toolExecutor.ExecuteAsync(toolCall, context, ct);
        return McpAdapter.ToMcpResult(result);
    }
}
