using Edda.AKG.Mcp.Models;

namespace Edda.AKG.Mcp.Server;

/// <summary>
/// Exposes the allow-listed subset of internal tools as MCP tool definitions and guards which tools
/// may be invoked via MCP.
/// </summary>
public interface IMcpToolRegistry
{
    /// <summary>Returns the allow-listed internal tools in their MCP protocol representation.</summary>
    IReadOnlyList<McpToolDefinition> GetMcpTools();

    /// <summary>Returns true if the named tool may be exposed/invoked via MCP.</summary>
    /// <param name="toolName">The tool name requested by an external client.</param>
    bool IsExposed(string? toolName);
}
