using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Tracks taint labels on tool results and enforces data-flow restrictions before
/// tool execution. Integrated into <see cref="IToolExecutor"/> (ToolRegistry) via
/// <see cref="ToolExecutionContext.TaintTracker"/>.
/// A fresh instance is created per agent turn and passed through the execution context,
/// ensuring isolation between concurrent user sessions.
/// </summary>
public interface ITaintTracker
{
    /// <summary>
    /// Records the taint label of a completed tool result so subsequent calls can
    /// detect data-flow violations that would reference this result.
    /// </summary>
    /// <param name="toolCallId">The correlation ID of the tool call that produced this result.</param>
    /// <param name="label">The taint label to associate with this result.</param>
    void RecordResult(string toolCallId, TaintLabel label);

    /// <summary>
    /// Checks whether a tool call is allowed given the taint labels of arguments
    /// that reference previous tool results.
    /// </summary>
    /// <param name="call">The tool call about to be executed.</param>
    /// <returns>
    /// A <see cref="TaintCheckResult"/> indicating whether the call is allowed
    /// and, if not, which label and sink rule caused the violation.
    /// </returns>
    TaintCheckResult Check(ToolCall call);

    /// <summary>
    /// Explicitly removes the taint restriction from a tool-call result, allowing
    /// downstream tools to receive the data. Requires a justification which is written
    /// to the audit log.
    /// </summary>
    /// <param name="toolCallId">The tool call result ID to declassify.</param>
    /// <param name="justification">Audit-logged reason for the declassification.</param>
    void Declassify(string toolCallId, string justification);

    /// <summary>
    /// Resets all taint state. Called at the start of each agent turn to ensure
    /// no stale labels carry over between turns.
    /// </summary>
    void Reset();
}
