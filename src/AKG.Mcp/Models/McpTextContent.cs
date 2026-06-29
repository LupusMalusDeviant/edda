namespace Edda.AKG.Mcp.Models;

/// <summary>
/// Represents a text content block in an MCP tool result.
/// Corresponds to the MCP protocol content item with type="text".
/// </summary>
/// <param name="Text">The text content of the response.</param>
public sealed record McpTextContent(string Text)
{
    /// <summary>Always "text" per MCP protocol specification.</summary>
    public string Type => "text";
}
