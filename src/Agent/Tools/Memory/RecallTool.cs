using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Recalls the current user's episodic memories relevant to a query (M3 / ADR-0011). Read-only: retrieves
/// the user's <c>SourceType=memory</c> nodes via the graph's user-scoped rule query and ranks them by
/// keyword overlap with the query. Opt-in for MCP exposure (not in the default read allow-list).
/// </summary>
internal sealed class RecallTool : IAgentTool
{
    private const int MaxResults = 10;

    private readonly IKnowledgeGraph _graph;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecallTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "recall",
        Description = "Recall what you remembered for the current user that is relevant to a query. " +
                      "Returns your most relevant stored memories.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "What to recall: a topic, question, or keywords." }
            },
            required = new[] { "query" }
        }
    };

    /// <summary>Initializes a new <see cref="RecallTool"/>.</summary>
    /// <param name="knowledgeGraph">Graph the memory nodes are read from.</param>
    /// <param name="timeProvider">Provides the current time for the recall forgetting curve.</param>
    /// <param name="logger">Structured logger.</param>
    public RecallTool(IKnowledgeGraph knowledgeGraph, TimeProvider timeProvider, ILogger<RecallTool> logger)
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
            var query = ToolArgumentHelper.GetRequiredString(call.Arguments, "query");
            var userId = context.UserId ?? "anonymous";

            var memories = await _graph
                .GetRulesAsync(type: MemoryNodes.MemoryType, userId: userId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var owned = memories
                .Where(m => string.Equals(m.OwnerId, userId, StringComparison.Ordinal))
                .ToList();
            if (owned.Count == 0)
                return ToolResult.Ok(call.Id, Definition.Name, "No memories found.");

            var terms = ExtractTerms(query);
            var now = _timeProvider.GetUtcNow();
            var ranked = owned
                .Select(m => new
                {
                    Memory = m,
                    Recency = MemoryNodes.RecencyFactor(m.Created, now, MemoryNodes.DefaultDecayHalfLifeDays),
                    Keyword = RelevanceScore(m, terms),
                })
                .OrderByDescending(x => x.Keyword * x.Recency)
                .ThenByDescending(x => x.Recency)
                .Take(MaxResults)
                .Select(x => $"- {x.Memory.Body}")
                .ToList();

            _logger.LogInformation("recall query length={Len} userId={UserId} returned={Count}",
                query.Length, userId, ranked.Count);
            return ToolResult.Ok(call.Id, Definition.Name, string.Join('\n', ranked));
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "recall failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    private static IReadOnlyList<string> ExtractTerms(string query) =>
        query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
             .Select(w => w.Trim('.', ',', '!', '?', '"', '\'').ToLowerInvariant())
             .Where(w => w.Length > 2)
             .Distinct()
             .ToList();

    private static int RelevanceScore(KnowledgeRule memory, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return 0;
        var body = memory.Body.ToLowerInvariant();
        return terms.Count(term => body.Contains(term, StringComparison.Ordinal));
    }
}
