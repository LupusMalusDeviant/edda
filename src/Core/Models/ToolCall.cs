namespace Edda.Core.Models;

/// <summary>
/// Represents a tool invocation requested by the assistant during a conversation turn.
/// </summary>
public sealed record ToolCall
{
    /// <summary>Unique call identifier assigned by the LLM. Used to correlate with ToolResult.</summary>
    public required string Id { get; init; }

    /// <summary>Name of the tool to invoke. Must match a registered tool in the ToolRegistry.</summary>
    public required string Name { get; init; }

    /// <summary>Parsed JSON arguments as a key-value dictionary.</summary>
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }

    /// <summary>
    /// Google Gemini thought signature from the model response (OpenAI field:
    /// <c>extra_content.google.thought_signature</c>). Required on replay for Gemini 3+ tool turns.
    /// </summary>
    public string? ThoughtSignature { get; init; }
}
