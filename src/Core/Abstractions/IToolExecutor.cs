using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates tool execution including output filtering, audit logging, and timeout enforcement.
/// Used by AgentRuntime — tools should not call this directly.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes a single tool call:
    /// 1. Looks up the tool in the ToolRegistry.
    /// 2. Calls IAgentTool.ExecuteAsync() with a 90-second timeout.
    /// 3. Filters output through SecretRedactor.
    /// 4. Writes an audit log entry.
    /// </summary>
    /// <param name="call">The tool call to execute.</param>
    /// <param name="context">Execution context with identity and conversation data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The filtered tool result.</returns>
    Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes multiple tool calls in parallel (Task.WhenAll).
    /// Returns all results, including partial failures — never throws on individual tool failure.
    /// </summary>
    /// <param name="calls">Tool calls to execute concurrently.</param>
    /// <param name="context">Shared execution context for all calls.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for all calls, in the same order as the input list.</returns>
    Task<IReadOnlyList<ToolResult>> ExecuteManyAsync(
        IReadOnlyList<ToolCall> calls,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}
