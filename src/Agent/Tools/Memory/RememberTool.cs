using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Persists a single fact into the current user's episodic memory (M3 / ADR-0011) as a per-fact
/// <c>SourceType=memory</c> graph node. Idempotent: remembering the same fact upserts the same node.
/// A mutating tool — blocked from external MCP exposure by default (see <c>McpExposurePolicy</c>).
/// </summary>
internal sealed class RememberTool : IAgentTool
{
    private readonly IKnowledgeGraph _graph;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RememberTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "remember",
        Description = "Store a single fact in your long-term episodic memory for the current user, so you " +
                      "can recall it in later sessions. Remembering the same fact twice is idempotent.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                content = new { type = "string", description = "The fact to remember." }
            },
            required = new[] { "content" }
        }
    };

    /// <summary>Initializes a new <see cref="RememberTool"/>.</summary>
    /// <param name="knowledgeGraph">Graph the memory node is upserted into.</param>
    /// <param name="timeProvider">Provides the creation timestamp.</param>
    /// <param name="logger">Structured logger.</param>
    public RememberTool(IKnowledgeGraph knowledgeGraph, TimeProvider timeProvider, ILogger<RememberTool> logger)
    {
        _graph = knowledgeGraph;
        _timeProvider = timeProvider;
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
            if (string.IsNullOrWhiteSpace(content))
                return ToolResult.Fail(call.Id, Definition.Name, "Cannot remember empty content.");

            var rule = MemoryNodes.Create(userId, content, _timeProvider.GetUtcNow());
            await _graph.UpsertRuleAsync(rule, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("remember stored memory {MemoryId} for userId={UserId}", rule.Id, userId);
            return ToolResult.Ok(call.Id, Definition.Name, $"Remembered. (id: {rule.Id})");
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "remember failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }
}
