namespace Edda.AKG.Mcp.Models;

/// <summary>
/// Represents an MCP tool invocation request received from an external MCP client.
/// </summary>
public sealed record McpToolCall
{
    /// <summary>Optional call identifier. If null, a new GUID is assigned by McpAdapter.</summary>
    public string? Id { get; init; }

    /// <summary>Name of the tool to invoke.</summary>
    public required string Name { get; init; }

    /// <summary>Arguments passed to the tool as a key-value dictionary.</summary>
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
        new Dictionary<string, object?>();
}
