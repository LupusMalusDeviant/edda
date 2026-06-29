using Edda.AKG.Mcp.Models;

namespace Edda.AKG.Mcp.Client;

/// <summary>
/// Abstraction for a client that communicates with an external MCP server.
/// Allows <see cref="McpToolImporter"/> and <see cref="McpToolSource"/> to be tested
/// without a real network connection.
/// </summary>
public interface IExternalMcpClient
{
    /// <summary>
    /// Retrieves the list of tools advertised by the external MCP server.
    /// Performs an MCP <c>tools/list</c> request.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All tool definitions provided by the remote server.</returns>
    Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken ct);

    /// <summary>
    /// Invokes a named tool on the external MCP server.
    /// Performs an MCP <c>tools/call</c> request.
    /// </summary>
    /// <param name="name">The tool name to invoke.</param>
    /// <param name="arguments">Arguments to pass to the tool.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tool result returned by the remote server.</returns>
    Task<McpToolResult> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);
}
