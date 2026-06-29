using System.Text.Json;
using Edda.AKG.Mcp.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Edda.AKG.Mcp.Server;

/// <summary>
/// Bridges the internal tool layer to the official MCP SDK server handlers (<c>tools/list</c> and
/// <c>tools/call</c>), advertising only allow-listed tools.
/// </summary>
public interface IMcpProtocolHandlers
{
    /// <summary>Builds the SDK tool definitions for all allow-listed internal tools.</summary>
    IReadOnlyList<Tool> BuildExposedTools();

    /// <summary>Invokes an allow-listed tool through the guarded server.</summary>
    /// <param name="toolName">The requested tool name.</param>
    /// <param name="arguments">The MCP arguments as JSON elements, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The internal tool result.</returns>
    Task<McpToolResult> InvokeAsync(string? toolName, IDictionary<string, JsonElement>? arguments, CancellationToken ct);

    /// <summary>Handler delegate for MCP <c>tools/list</c> requests.</summary>
    ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> context, CancellationToken cancellationToken);

    /// <summary>Handler delegate for MCP <c>tools/call</c> requests.</summary>
    ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken);
}
