using Edda.AKG.Mcp.Models;
using Edda.Core.Models;

namespace Edda.AKG.Mcp.Server;

/// <summary>
/// Guarded MCP server exposing internal tools to external clients via the <c>tools/list</c> and
/// <c>tools/call</c> operations. Routes every call through the internal tool executor.
/// </summary>
public interface IMcpServer
{
    /// <summary>Returns all allow-listed tools as MCP definitions (<c>tools/list</c>).</summary>
    IReadOnlyList<McpToolDefinition> ListTools();

    /// <summary>Executes a tool invocation from an external MCP client (<c>tools/call</c>).</summary>
    /// <param name="mcpCall">The MCP tool call.</param>
    /// <param name="context">Execution context for the call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The MCP tool result including content and error status.</returns>
    Task<McpToolResult> CallToolAsync(McpToolCall mcpCall, ToolExecutionContext context, CancellationToken ct = default);
}
