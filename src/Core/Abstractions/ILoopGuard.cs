using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Evaluates whether the current tool-call sequence constitutes a detected loop
/// and returns a graduated verdict indicating how the ToolLoop should respond.
/// </summary>
/// <remarks>
/// Implementations are stateful and track invocation history across the calls within a
/// single agent turn. <see cref="Reset"/> must be called at the start of each new turn
/// to discard stale history. The interface is designed so implementations can be created
/// per-turn (new instance) or reused across turns with explicit resets.
/// </remarks>
public interface ILoopGuard
{
    /// <summary>
    /// Records a completed tool invocation (call + result) and evaluates the current
    /// sequence for repetition patterns. Must be called after each tool execution.
    /// </summary>
    /// <param name="call">The tool call that was executed.</param>
    /// <param name="result">The result returned by the tool.</param>
    /// <returns>
    /// A <see cref="LoopGuardVerdict"/> indicating whether the ToolLoop should continue
    /// (<see cref="LoopGuardDecision.Allow"/>), inject a warning
    /// (<see cref="LoopGuardDecision.Warn"/>), stop gracefully
    /// (<see cref="LoopGuardDecision.Block"/>), or abort immediately
    /// (<see cref="LoopGuardDecision.CircuitBreak"/>).
    /// </returns>
    LoopGuardVerdict Evaluate(ToolCall call, ToolResult result);

    /// <summary>
    /// Resets all internal state. Called at the start of each new agent turn to ensure
    /// history from previous turns does not influence the current evaluation.
    /// </summary>
    void Reset();
}
