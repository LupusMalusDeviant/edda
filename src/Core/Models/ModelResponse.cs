namespace Edda.Core.Models;

/// <summary>
/// Raw response from an LLM provider, before pipeline post-processing.
/// </summary>
public sealed record ModelResponse
{
    /// <summary>Generated text content. May be empty when the model only issued tool calls.</summary>
    public required string Content { get; init; }

    /// <summary>Tool calls requested by the model in this response.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];

    /// <summary>
    /// Reason the model stopped generating.
    /// Typical values: "end_turn", "tool_use", "max_tokens".
    /// </summary>
    public required string StopReason { get; init; }

    /// <summary>Token consumption for this completion, if reported by the provider.</summary>
    public TokenUsage? Usage { get; init; }
}

/// <summary>Token consumption for a single model completion.</summary>
/// <param name="InputTokens">Tokens consumed by the prompt and system message.</param>
/// <param name="OutputTokens">Tokens generated in the response.</param>
public sealed record TokenUsage(int InputTokens, int OutputTokens);

/// <summary>Metadata about a model available from a provider.</summary>
/// <param name="Id">Provider-specific model identifier (e.g. "claude-sonnet-4-6").</param>
/// <param name="DisplayName">Human-readable name for display in the UI.</param>
/// <param name="SupportsTools">True if the model supports tool/function calling.</param>
public sealed record ModelInfo(string Id, string DisplayName, bool SupportsTools);
