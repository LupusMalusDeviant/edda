using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Base interface for all agent tools. Implementations register themselves in the ToolRegistry.
/// External MCP tools are also wrapped as IAgentTool via McpToolSource.
/// Tools must never throw — all errors must be returned as ToolResult.Fail().
/// </summary>
public interface IAgentTool
{
    /// <summary>
    /// Tool definition including name, description, and input schema.
    /// Passed to the LLM to enable tool selection.
    /// </summary>
    ToolDefinition Definition { get; }

    /// <summary>
    /// Executes the tool. Always returns a ToolResult — never throws.
    /// Errors are communicated via ToolResult.Fail(callId, toolName, message).
    /// </summary>
    /// <param name="call">The tool call issued by the model, including arguments.</param>
    /// <param name="context">Execution context with conversation ID, user ID, and channel metadata.</param>
    /// <param name="cancellationToken">Cancellation token. Respect it for long-running operations.</param>
    /// <returns>A ToolResult indicating success or failure.</returns>
    Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}
