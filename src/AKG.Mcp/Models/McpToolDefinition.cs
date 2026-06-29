namespace Edda.AKG.Mcp.Models;

/// <summary>
/// Describes a tool exposed via the Model Context Protocol (MCP).
/// Used by both the MCP server (to advertise internal tools) and the MCP client (to receive external tool definitions).
/// </summary>
public sealed record McpToolDefinition
{
    /// <summary>The unique snake_case name of the tool.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable description shown to the LLM for tool selection.</summary>
    public required string Description { get; init; }

    /// <summary>JSON Schema object describing the tool's input parameters.</summary>
    public required object InputSchema { get; init; }
}
