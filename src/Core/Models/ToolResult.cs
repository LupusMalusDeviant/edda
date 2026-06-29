namespace Edda.Core.Models;

/// <summary>
/// Represents the outcome of a tool execution, returned to the LLM.
/// Tools never throw exceptions — errors are always represented as ToolResult with Success=false.
/// </summary>
public sealed record ToolResult
{
    /// <summary>
    /// Echo of ToolCall.Id. Required by LLM protocols to correlate results with requests.
    /// </summary>
    public required string ToolCallId { get; init; }

    /// <summary>Tool name. Required by some provider APIs (e.g. Google).</summary>
    public required string Name { get; init; }

    /// <summary>True if the tool executed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Filtered output content. Secrets are already redacted by the time this is set.
    /// Null when Success=false.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>Error description. Only set when Success=false.</summary>
    public string? Error { get; init; }

    /// <summary>Creates a successful tool result with the given content.</summary>
    /// <param name="toolCallId">The correlation ID from the original ToolCall.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="content">The tool output (already filtered).</param>
    /// <returns>A successful ToolResult.</returns>
    public static ToolResult Ok(string toolCallId, string name, string content) =>
        new() { ToolCallId = toolCallId, Name = name, Success = true, Content = content };

    /// <summary>Creates a failed tool result with an error message.</summary>
    /// <param name="toolCallId">The correlation ID from the original ToolCall.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="error">Human-readable error description.</param>
    /// <returns>A failed ToolResult.</returns>
    public static ToolResult Fail(string toolCallId, string name, string error) =>
        new() { ToolCallId = toolCallId, Name = name, Success = false, Error = error };
}
