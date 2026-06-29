namespace Edda.Core.Models;

/// <summary>
/// Result of an MCP tool call.
/// </summary>
/// <param name="Success">Whether the tool call succeeded.</param>
/// <param name="Text">Response text from the MCP tool.</param>
public sealed record McpCallResult(bool Success, string Text);
