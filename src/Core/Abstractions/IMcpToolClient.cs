using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Minimal abstraction for calling MCP tools.
/// Allows Agent layer to invoke MCP operations without depending on AKG.Mcp.
/// </summary>
public interface IMcpToolClient
{
    /// <summary>
    /// Lists all tools available on the MCP server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of tool names.</returns>
    Task<IReadOnlyList<string>> ListToolNamesAsync(CancellationToken ct);

    /// <summary>
    /// Calls a named tool with arguments and returns the text result.
    /// </summary>
    /// <param name="toolName">Name of the MCP tool to invoke.</param>
    /// <param name="arguments">Tool arguments as key-value pairs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with success flag and response text.</returns>
    Task<McpCallResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);
}
