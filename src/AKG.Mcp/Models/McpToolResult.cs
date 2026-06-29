namespace Edda.AKG.Mcp.Models;

/// <summary>
/// Represents the result of an MCP tool call returned to the MCP client.
/// Contains the output content items and an error flag.
/// </summary>
public sealed record McpToolResult
{
    /// <summary>List of content items produced by the tool invocation.</summary>
    public required IReadOnlyList<McpTextContent> Content { get; init; }

    /// <summary>True if the tool execution resulted in an error.</summary>
    public bool IsError { get; init; }
}
