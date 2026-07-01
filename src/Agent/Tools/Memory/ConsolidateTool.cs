using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Consolidates the current user's episodic memory (M3 / ADR-0011): removes normalized-duplicate memories
/// (keeping the most recent of each set) and prunes memories that have faded below a recall-relevance
/// threshold. Purely deterministic — no LLM. A mutating tool — blocked from external MCP exposure by
/// default (see <c>McpExposurePolicy</c>).
/// </summary>
internal sealed class ConsolidateTool : IAgentTool
{
    // Memories whose recency weight has fallen to/below this are considered forgotten and pruned.
    private const double PruneThreshold = 0.05;

    private readonly IKnowledgeGraph _graph;
    private readonly TimeProvider _timeProvider;
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
    /// <param name="knowledgeGraph">Graph the memory nodes are read from and pruned in.</param>
    /// <param name="timeProvider">Provides the current time for the fade threshold.</param>
    /// <param name="logger">Structured logger.</param>
    public ConsolidateTool(IKnowledgeGraph knowledgeGraph, TimeProvider timeProvider, ILogger<ConsolidateTool> logger)
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

            var memories = await _graph
                .GetRulesAsync(type: MemoryNodes.MemoryType, userId: userId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var owned = memories
                .Where(m => string.Equals(m.OwnerId, userId, StringComparison.Ordinal))
                .ToList();

            var duplicates = FindDuplicates(owned);
            var duplicateIds = duplicates.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

            var now = _timeProvider.GetUtcNow();
            var faded = owned
                .Where(m => !duplicateIds.Contains(m.Id))
                .Where(m => MemoryNodes.RecencyFactor(m.Created, now, MemoryNodes.DefaultDecayHalfLifeDays) <= PruneThreshold)
                .ToList();

            foreach (var memory in duplicates.Concat(faded))
                await _graph.DeleteRuleAsync(memory.Id, userId, isAdmin: false, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("consolidate_memory userId={UserId} duplicates={Dup} faded={Faded}",
                userId, duplicates.Count, faded.Count);
            return ToolResult.Ok(call.Id, Definition.Name,
                $"Consolidated memory: removed {duplicates.Count} duplicate(s), forgot {faded.Count} faded memory(ies).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "consolidate_memory failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    /// <summary>
    /// Returns the redundant memories in <paramref name="owned"/>: within each set of memories whose content
    /// normalizes to the same text, every entry except the most recently created is redundant.
    /// </summary>
    /// <param name="owned">The user's memory nodes.</param>
    /// <returns>The redundant memories to remove.</returns>
    private static IReadOnlyList<KnowledgeRule> FindDuplicates(IReadOnlyList<KnowledgeRule> owned)
    {
        var redundant = new List<KnowledgeRule>();
        foreach (var group in owned.GroupBy(m => MemoryNodes.Normalize(m.Body)))
        {
            var members = group.OrderByDescending(m => m.Created ?? DateOnly.MinValue).ToList();
            if (members.Count <= 1)
                continue;
            redundant.AddRange(members.Skip(1));
        }

        return redundant;
    }
}
