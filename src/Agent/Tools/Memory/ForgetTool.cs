using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Forgets a single fact from the current user's episodic memory (M3 / ADR-0011) by deleting its
/// content-addressed <c>SourceType=memory</c> node. Idempotent: forgetting an unknown fact is a no-op.
/// A mutating tool — blocked from external MCP exposure by default (see <c>McpExposurePolicy</c>).
/// </summary>
internal sealed class ForgetTool : IAgentTool
{
    private readonly IKnowledgeGraph _graph;
    private readonly ILogger<ForgetTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "forget",
        Description = "Delete a single previously remembered fact from the current user's episodic memory. " +
                      "Pass the same content that was remembered.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                content = new { type = "string", description = "The exact fact to forget." }
            },
            required = new[] { "content" }
        }
    };

    /// <summary>Initializes a new <see cref="ForgetTool"/>.</summary>
    /// <param name="knowledgeGraph">Graph the memory node is deleted from.</param>
    /// <param name="logger">Structured logger.</param>
    public ForgetTool(IKnowledgeGraph knowledgeGraph, ILogger<ForgetTool> logger)
    {
        _graph = knowledgeGraph;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = context.UserId ?? "anonymous";
            var content = ToolArgumentHelper.GetRequiredString(call.Arguments, "content");
            var id = MemoryNodes.NodeId(userId, content);

            var existing = await _graph.GetRuleAsync(id, userId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
                return ToolResult.Ok(call.Id, Definition.Name, "No matching memory to forget.");

            await _graph.DeleteRuleAsync(id, userId, isAdmin: false, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("forget removed memory {MemoryId} for userId={UserId}", id, userId);
            return ToolResult.Ok(call.Id, Definition.Name, "Forgotten.");
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "forget failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }
}
