using Edda.Core.Abstractions;

namespace Edda.Core.Models;

/// <summary>
/// Incoming request to the agent runtime. Built by each channel before invoking IAgentRuntime.
/// </summary>
public sealed record AgentRequest
{
    /// <summary>The raw user message text.</summary>
    public required string UserMessage { get; init; }

    /// <summary>Identifies the conversation session. Used to scope history and tool context.</summary>
    public required string ConversationId { get; init; }

    /// <summary>Resolved identity of the requesting user.</summary>
    public required IIdentityContext Identity { get; init; }

    /// <summary>
    /// Channel-specific metadata injected by the originating channel.
    /// Example keys: telegram_chat_id, channel.
    /// </summary>
    public IReadOnlyDictionary<string, string> ChannelMetadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Optional multimodal content parts for the current user turn (text + images).
    /// When set, the runtime passes these to the provider instead of the plain
    /// <see cref="UserMessage"/> string, enabling vision capabilities.
    /// The text component mirrors the sanitized <see cref="UserMessage"/>.
    /// <para>
    /// Null means text-only — the provider receives <see cref="UserMessage"/> as a plain string.
    /// </para>
    /// </summary>
    public IReadOnlyList<ContentPart>? ContentParts { get; init; }

    /// <summary>
    /// Optional callback invoked by the agent runtime before and after each tool call
    /// during Phase 6 (tool loop). Channels can use this to render live progress indicators
    /// (e.g. a Telegram message that is edited as each tool call starts and resolves).
    /// </summary>
    public IProgress<ToolProgressEvent>? OnToolProgress { get; init; }
}

/// <summary>
/// Completed response from the agent runtime after executing all pipeline phases.
/// </summary>
public sealed record AgentResponse
{
    /// <summary>Final text content produced by the agent.</summary>
    public required string Content { get; init; }

    /// <summary>The conversation ID this response belongs to.</summary>
    public required string ConversationId { get; init; }

    /// <summary>True if the RepetitionDetector truncated the response due to detected repetition.</summary>
    public bool WasRepetitionDetected { get; init; }

    /// <summary>True if TDK validation found at least one rule violation (may have triggered re-query).</summary>
    public bool WasTdkViolationFound { get; init; }

    /// <summary>IDs of AKG rules that were active during this turn.</summary>
    public IReadOnlyList<string> ActiveRuleIds { get; init; } = [];
}
