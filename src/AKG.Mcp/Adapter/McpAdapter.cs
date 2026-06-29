using Edda.AKG.Mcp.Models;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Mcp.Adapter;

/// <summary>
/// Static converter between Edda core types and MCP protocol types.
/// Provides bidirectional mapping for tool definitions, calls, and results.
/// </summary>
public static class McpAdapter
{
    /// <summary>
    /// Converts an <see cref="IAgentTool"/> to its MCP tool definition representation.
    /// </summary>
    /// <param name="tool">The internal agent tool to convert.</param>
    /// <returns>An MCP tool definition with name, description, and input schema.</returns>
    public static McpToolDefinition ToMcpTool(IAgentTool tool) =>
        ToMcpTool(tool.Definition);

    /// <summary>
    /// Converts a <see cref="ToolDefinition"/> to its MCP tool definition representation.
    /// </summary>
    /// <param name="definition">The tool definition to convert.</param>
    /// <returns>An MCP tool definition with name, description, and input schema.</returns>
    public static McpToolDefinition ToMcpTool(ToolDefinition definition) => new()
    {
        Name = definition.Name,
        Description = definition.Description,
        InputSchema = definition.ToJsonSchema()
    };

    /// <summary>
    /// Converts an incoming MCP tool call to the internal <see cref="ToolCall"/> model.
    /// Assigns a new GUID as the call ID if none is provided.
    /// </summary>
    /// <param name="mcpCall">The MCP tool call received from an external client.</param>
    /// <returns>An internal tool call ready for execution.</returns>
    public static ToolCall FromMcpCall(McpToolCall mcpCall) => new()
    {
        Id = mcpCall.Id ?? Guid.NewGuid().ToString(),
        Name = mcpCall.Name,
        Arguments = mcpCall.Arguments
    };

    /// <summary>
    /// Converts an internal <see cref="ToolResult"/> to an MCP tool result for the external client.
    /// Sets <see cref="McpToolResult.IsError"/> based on whether the execution succeeded.
    /// </summary>
    /// <param name="result">The internal tool result to convert.</param>
    /// <returns>An MCP tool result containing the content or error text.</returns>
    public static McpToolResult ToMcpResult(ToolResult result) => new()
    {
        Content = [new McpTextContent(result.Content ?? result.Error ?? "")],
        IsError = !result.Success
    };
}
