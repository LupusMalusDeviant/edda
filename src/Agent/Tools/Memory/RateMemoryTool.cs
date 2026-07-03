using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Memory;

/// <summary>
/// Records an agent's usefulness rating for a single AKG rule (E2) into the feedback/confidence layer so the
/// graph learns which rules to trust. A mutating tool — blocked from external MCP exposure by default
/// (see <c>McpExposurePolicy.WriteToolNames</c>). Never throws (Regel 5); the owner is taken from the tool
/// execution context (Regel 6).
/// </summary>
internal sealed class RateMemoryTool : IAgentTool
{
    private readonly IRuleFeedbackService _feedback;
    private readonly ILogger<RateMemoryTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "rate_memory",
        Description = "Give feedback on how useful a specific knowledge rule (by its id) was, so the graph " +
                      "learns which rules to trust. outcome is one of: helpful, not_helpful, outdated.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                ruleId = new { type = "string", description = "The id of the rule to rate." },
                outcome = new
                {
                    type = "string",
                    description = "How useful the rule was: 'helpful', 'not_helpful', or 'outdated'.",
                }
            },
            required = new[] { "ruleId", "outcome" }
        }
    };

    /// <summary>Initializes a new <see cref="RateMemoryTool"/>.</summary>
    /// <param name="feedback">Feedback service the rating is recorded into.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="authorizer">C2: central role gate — writing requires Editor. Null permits (legacy).</param>
    public RateMemoryTool(IRuleFeedbackService feedback, ILogger<RateMemoryTool> logger,
        IRuleAuthorizer? authorizer = null)
    {
        _feedback = feedback;
        _logger = logger;
        _authorizer = authorizer;
    }

    private readonly IRuleAuthorizer? _authorizer;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call, ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // C2: mutating tool — the role gate rejects Viewers before any argument is parsed.
            if (!MemoryToolAuthorization.MayMutate(_authorizer))
                return ToolResult.Fail(call.Id, Definition.Name, MemoryToolAuthorization.InsufficientRoleMessage);

            var userId = context.UserId ?? "anonymous";

            var ruleId = ToolArgumentHelper.GetRequiredString(call.Arguments, "ruleId");
            if (string.IsNullOrWhiteSpace(ruleId))
                return ToolResult.Fail(call.Id, Definition.Name, "ruleId must not be empty.");

            var outcome = ToolArgumentHelper.GetRequiredString(call.Arguments, "outcome");
            if (!TryParseRating(outcome, out var rating))
                return ToolResult.Fail(
                    call.Id, Definition.Name, "outcome must be one of: helpful, not_helpful, outdated.");

            await _feedback.RecordRuleRatingAsync(ruleId, rating, userId, cancellationToken).ConfigureAwait(false);
            return ToolResult.Ok(call.Id, Definition.Name, $"Recorded '{outcome}' for rule '{ruleId}'.");
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "rate_memory failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    private static bool TryParseRating(string outcome, out RuleRating rating)
    {
        switch (outcome.Trim().ToLowerInvariant())
        {
            case "helpful":
                rating = RuleRating.Helpful;
                return true;
            case "not_helpful":
            case "nothelpful":
                rating = RuleRating.NotHelpful;
                return true;
            case "outdated":
                rating = RuleRating.Outdated;
                return true;
            default:
                rating = default;
                return false;
        }
    }
}
