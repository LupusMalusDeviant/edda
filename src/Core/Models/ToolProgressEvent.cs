namespace Edda.Core.Models;

/// <summary>
/// Execution phase of a single tool call, reported via
/// <see cref="AgentRequest.OnToolProgress"/> during a live agent turn.
/// </summary>
public enum ToolProgressStatus
{
    /// <summary>The tool has been dispatched and is currently running.</summary>
    Started,

    /// <summary>The tool completed successfully.</summary>
    Completed,

    /// <summary>The tool returned an error or threw an exception.</summary>
    Failed,
}

/// <summary>
/// Emitted by the agent runtime via <see cref="AgentRequest.OnToolProgress"/>
/// whenever a tool call starts or finishes during Phase 6 (tool loop).
/// Channels can use this to render live progress indicators (e.g. a Telegram
/// message that is edited as each tool call resolves).
/// </summary>
public sealed record ToolProgressEvent
{
    /// <summary>Internal name of the tool being executed (e.g. <c>web_search</c>).</summary>
    public required string ToolName { get; init; }

    /// <summary>Current execution phase of the tool call.</summary>
    public required ToolProgressStatus Status { get; init; }

    /// <summary>
    /// Zero-based index of the tool-loop iteration in which this tool was called.
    /// Useful for grouping multiple tool calls across iterations.
    /// </summary>
    public int Iteration { get; init; }
}
