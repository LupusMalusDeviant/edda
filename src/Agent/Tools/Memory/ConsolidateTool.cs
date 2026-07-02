using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Consolidates the current user's episodic memory (M3 / ADR-0011): removes normalized-duplicate memories
/// (keeping the most recent) and prunes memories that have faded below a recall-relevance threshold. Purely
/// deterministic — no LLM. A mutating tool — blocked from external MCP exposure by default (see
/// <c>McpExposurePolicy</c>). Delegates to the shared <see cref="IMemoryConsolidator"/> so the tool and the
/// periodic background maintenance (issue C10) run the same logic.
/// </summary>
internal sealed class ConsolidateTool : IAgentTool
{
    /// <summary>Maximum number of merged-away fact bodies named in the tool response before summarising the rest.</summary>
    private const int MaxMergedListed = 3;

    private readonly IMemoryConsolidator _consolidator;
    private readonly ILogger<ConsolidateTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "consolidate_memory",
        Description = "Tidy the current user's episodic memory: drop duplicate memories and forget entries " +
                      "that have faded away. Safe to call at the end of a session.",
        InputSchema = new
        {
            type = "object",
            properties = new { }
        }
    };

    /// <summary>Initializes a new <see cref="ConsolidateTool"/>.</summary>
    /// <param name="consolidator">Shared consolidation logic.</param>
    /// <param name="logger">Structured logger.</param>
    public ConsolidateTool(IMemoryConsolidator consolidator, ILogger<ConsolidateTool> logger)
    {
        _consolidator = consolidator;
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
            var result = await _consolidator.ConsolidateUserAsync(userId, cancellationToken).ConfigureAwait(false);

            var message = $"Consolidated memory: removed {result.DuplicatesRemoved} duplicate(s), "
                + $"{result.NearDuplicatesRemoved} near-duplicate(s), "
                + $"forgot {result.FadedRemoved} faded memory(ies).";
            if (result.MergedAwayBodies.Count > 0)
                message += " Merged away: " + SummariseMerged(result.MergedAwayBodies);
            return ToolResult.Ok(call.Id, Definition.Name, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "consolidate_memory failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    /// <summary>Formats up to <see cref="MaxMergedListed"/> merged-away near-duplicate bodies for the tool response.</summary>
    /// <param name="bodies">The near-duplicate loser bodies.</param>
    /// <returns>A short human-readable summary.</returns>
    private static string SummariseMerged(IReadOnlyList<string> bodies)
    {
        var named = string.Join("; ", bodies.Take(MaxMergedListed));
        var extra = bodies.Count - MaxMergedListed;
        return extra > 0 ? $"{named} (+{extra} more)" : named;
    }
}
