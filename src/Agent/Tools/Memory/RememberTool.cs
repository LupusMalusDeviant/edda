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
    private readonly double _jaccardThreshold;
    private readonly ILogger<RememberTool> _logger;

    /// <summary>Maximum number of superseded facts named in the tool response before summarising the rest.</summary>
    private const int MaxSupersededListed = 3;

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
    /// <param name="jaccardThreshold">
    /// C3: token-Jaccard threshold above which an existing memory is treated as superseded by the new fact.
    /// A value greater than 1.0 disables contradiction detection. Defaults to 0.6.
    /// </param>
    /// <param name="authorizer">C2: central role gate — writing requires Editor. Null permits (legacy).</param>
    public RememberTool(
        IKnowledgeGraph knowledgeGraph,
        TimeProvider timeProvider,
        ILogger<RememberTool> logger,
        double jaccardThreshold = 0.6,
        IRuleAuthorizer? authorizer = null)
    {
        _graph = knowledgeGraph;
        _timeProvider = timeProvider;
        _jaccardThreshold = jaccardThreshold;
        _logger = logger;
        _authorizer = authorizer;
    }

    private readonly IRuleAuthorizer? _authorizer;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // C2: mutating tool — the role gate rejects Viewers before any argument is parsed.
            if (!MemoryToolAuthorization.MayMutate(_authorizer))
                return ToolResult.Fail(call.Id, Definition.Name, MemoryToolAuthorization.InsufficientRoleMessage);

            var userId = context.UserId ?? "anonymous";
            var content = ToolArgumentHelper.GetRequiredString(call.Arguments, "content");
            if (string.IsNullOrWhiteSpace(content))
                return ToolResult.Fail(call.Id, Definition.Name, "Cannot remember empty content.");

            var newId = MemoryNodes.NodeId(userId, content);

            // C3: find existing memories of this user that the new fact likely supersedes (high token overlap).
            var superseded = await FindSupersededAsync(userId, content, newId, cancellationToken).ConfigureAwait(false);

            var rule = MemoryNodes.Create(userId, content, _timeProvider.GetUtcNow());
            if (superseded.Count > 0)
                rule = rule with { RelatesTo = new RuleRelations { Supersedes = superseded.Select(m => m.Id).ToList() } };

            await _graph.UpsertRuleAsync(rule, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("remember stored memory {MemoryId} for userId={UserId} (supersedes {Count})",
                rule.Id, userId, superseded.Count);

            var message = $"Remembered. (id: {rule.Id})";
            if (superseded.Count > 0)
                message += " Possibly supersedes: " + SummariseSuperseded(superseded);
            return ToolResult.Ok(call.Id, Definition.Name, message);
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

    /// <summary>
    /// Finds the user's existing memories that the new fact likely supersedes: token-Jaccard overlap above
    /// the configured threshold, excluding the new fact's own (idempotent) node. Best-effort — a lookup
    /// failure returns an empty list so remembering never fails because of contradiction detection.
    /// </summary>
    /// <param name="userId">The memory owner.</param>
    /// <param name="content">The new fact.</param>
    /// <param name="newId">The new fact's deterministic node id (excluded from matches).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The superseded memories, or an empty list.</returns>
    private async Task<IReadOnlyList<KnowledgeRule>> FindSupersededAsync(
        string userId, string content, string newId, CancellationToken ct)
    {
        if (_jaccardThreshold > 1.0)
            return [];

        try
        {
            var existing = await _graph
                .GetRulesAsync(type: MemoryNodes.MemoryType, userId: userId, cancellationToken: ct)
                .ConfigureAwait(false);

            return existing
                .Where(m => string.Equals(m.OwnerId, userId, StringComparison.Ordinal)
                            && !string.Equals(m.Id, newId, StringComparison.Ordinal)
                            && MemorySimilarity.Jaccard(content, m.Body) > _jaccardThreshold)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "remember: superseded-memory lookup failed; storing without supersede edge");
            return [];
        }
    }

    /// <summary>Formats up to <see cref="MaxSupersededListed"/> superseded fact bodies for the tool response.</summary>
    /// <param name="superseded">The superseded memories.</param>
    /// <returns>A short human-readable summary.</returns>
    private static string SummariseSuperseded(IReadOnlyList<KnowledgeRule> superseded)
    {
        var named = string.Join("; ", superseded.Take(MaxSupersededListed).Select(m => m.Body));
        var extra = superseded.Count - MaxSupersededListed;
        return extra > 0 ? $"{named} (+{extra} more)" : named;
    }
}
