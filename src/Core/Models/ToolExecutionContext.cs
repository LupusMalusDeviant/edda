using Edda.Core.Abstractions;

namespace Edda.Core.Models;

/// <summary>
/// Contextual information passed to a tool during execution.
/// Contains identity, conversation, channel-specific metadata, and optional taint tracking.
/// </summary>
public sealed record ToolExecutionContext
{
    /// <summary>Identifies the conversation this tool execution belongs to.</summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// User ID from IIdentityContext. Never read from tool arguments — always from this property.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>True if the tool output should be executed inside an isolated sandbox.</summary>
    public bool Sandbox { get; init; }

    /// <summary>
    /// Channel-specific metadata injected by the originating channel.
    /// Examples: telegram_chat_id, channel="telegram"|"api"|"cli".
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Per-turn taint tracker for data-flow security enforcement.
    /// When non-null, <see cref="IToolExecutor"/> checks taint labels before execution
    /// and records labels after. Null when taint tracking is disabled via configuration.
    /// </summary>
    public ITaintTracker? TaintTracker { get; init; }

    /// <summary>
    /// Optional callback invoked by the tool loop before and after each tool call.
    /// Propagated from <see cref="AgentRequest.OnToolProgress"/>.
    /// Null when the originating channel does not require live progress updates.
    /// </summary>
    public IProgress<ToolProgressEvent>? OnToolProgress { get; init; }
}
