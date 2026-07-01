using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Agent.Tools.Knowledge;

/// <summary>
/// Reports coverage gaps in the knowledge graph so an operator or agent can see what is missing or
/// degrading: thinly-covered domains, broken references between entries, unresolved conflicts,
/// low-confidence entries, and stale entries whose feedback has aged out. Read-only and advisory —
/// it never modifies the graph. Exposed to MCP clients as the <c>analyze_coverage</c> tool (opt-in via
/// the exposure allowlist; it is not a write tool, so no write access is required).
/// </summary>
internal sealed class AnalyzeCoverageTool : IAgentTool
{
    // A domain with at most this many rules is flagged as thinly covered.
    private const int ThinDomainMaxRules = 2;

    // Rules whose confidence multiplier is below this are flagged as low-confidence.
    private const double LowConfidenceThreshold = 0.7;

    // Default staleness window (days) when the caller does not pass stale_days.
    private const int DefaultStaleDays = 90;

    // Cap on how many items are listed per category to keep the result compact; counts stay exact.
    private const int MaxItemsPerCategory = 50;

    private readonly IKnowledgeGraph _kg;
    private readonly IRuleFeedbackService _feedback;
    private readonly TimeProvider _time;
    private readonly ILogger<AnalyzeCoverageTool> _logger;

    /// <inheritdoc />
    public ToolDefinition Definition { get; } = new()
    {
        Name = "analyze_coverage",
        Description = "Analyse your long-term memory for coverage gaps: thinly-covered topics/domains, " +
                      "broken references between entries, unresolved conflicts, low-confidence entries, and " +
                      "stale entries whose feedback has aged out. Read-only; returns a JSON report so you can " +
                      "decide what knowledge to add, revisit, or reconcile.",
        InputSchema = new
        {
            type = "object",
            properties = new
            {
                stale_days = new
                {
                    type = "integer",
                    description = "An entry counts as stale when its most recent feedback is older than this " +
                                  "many days. Omit for the default (90)."
                }
            }
        }
    };

    /// <summary>
    /// Initializes a new <see cref="AnalyzeCoverageTool"/>.
    /// </summary>
    /// <param name="knowledgeGraph">The AKG used to read rules and their relations.</param>
    /// <param name="feedback">Feedback service used to read confidence and last-feedback timestamps.</param>
    /// <param name="time">Time provider used to measure staleness.</param>
    /// <param name="logger">Structured logger.</param>
    public AnalyzeCoverageTool(
        IKnowledgeGraph knowledgeGraph,
        IRuleFeedbackService feedback,
        TimeProvider time,
        ILogger<AnalyzeCoverageTool> logger)
    {
        _kg       = knowledgeGraph;
        _feedback = feedback;
        _time     = time;
        _logger   = logger;
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var staleDays = Math.Max(
                1, ToolArgumentHelper.GetInt(call.Arguments, "stale_days") ?? DefaultStaleDays);
            var userId = context.UserId;

            _logger.LogInformation(
                "analyze_coverage staleDays={StaleDays} userId={UserId}", staleDays, userId);

            var rules = await _kg.GetRulesAsync(null, null, null, userId, cancellationToken)
                .ConfigureAwait(false);
            var ruleIds = rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

            // ── Thinly-covered domains ──────────────────────────────────────────
            var thinDomains = rules
                .GroupBy(r => r.Domain, StringComparer.Ordinal)
                .Select(g => new { domain = g.Key, ruleCount = g.Count() })
                .Where(d => d.ruleCount <= ThinDomainMaxRules)
                .OrderBy(d => d.ruleCount)
                .ThenBy(d => d.domain, StringComparer.Ordinal)
                .ToList();

            // ── Broken references (relation targets that do not exist) ──────────
            var dangling = new List<object>();
            foreach (var rule in rules)
            {
                if (rule.RelatesTo is not { } rel) continue;
                foreach (var (relation, targets) in EnumerateRelations(rel))
                    foreach (var target in targets)
                        if (!ruleIds.Contains(target))
                            dangling.Add(new { ruleId = rule.Id, relation, missingTarget = target });
            }

            // ── Unresolved conflicts (both sides present; de-duplicated by pair) ─
            var conflicts = new List<object>();
            var seenPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                foreach (var target in rule.RelatesTo?.ConflictsWith ?? [])
                {
                    if (!ruleIds.Contains(target)) continue; // missing target already flagged as dangling
                    var pair = string.CompareOrdinal(rule.Id, target) <= 0
                        ? $"{rule.Id}|{target}"
                        : $"{target}|{rule.Id}";
                    if (seenPairs.Add(pair))
                        conflicts.Add(new { ruleId = rule.Id, conflictsWith = target });
                }
            }

            // ── Feedback-derived gaps: low confidence and staleness ─────────────
            var allStats = await _feedback.GetAllStatsAsync(cancellationToken).ConfigureAwait(false);
            var stats = allStats.Where(s => ruleIds.Contains(s.RuleId)).ToList();
            var now = _time.GetUtcNow();

            var lowConfidence = stats
                .Where(s => s.ConfidenceMultiplier < LowConfidenceThreshold)
                .OrderBy(s => s.ConfidenceMultiplier)
                .Select(s => new { ruleId = s.RuleId, multiplier = Math.Round(s.ConfidenceMultiplier, 2) })
                .ToList();

            var stale = stats
                .Where(s => s.LastFeedbackAt is { } last && (now - last).TotalDays > staleDays)
                .Select(s => new
                {
                    ruleId         = s.RuleId,
                    lastFeedbackAt = s.LastFeedbackAt!.Value.ToString("O"),
                    ageDays        = (int)(now - s.LastFeedbackAt!.Value).TotalDays
                })
                .OrderByDescending(x => x.ageDays)
                .ToList();

            var report = new
            {
                totalRules = rules.Count,
                domains    = rules.Select(r => r.Domain).Distinct(StringComparer.Ordinal).Count(),
                thresholds = new
                {
                    thinDomainMaxRules = ThinDomainMaxRules,
                    lowConfidenceBelow = LowConfidenceThreshold,
                    staleDays
                },
                thinDomains        = Capped(thinDomains),
                danglingReferences = Capped(dangling),
                conflicts          = Capped(conflicts),
                lowConfidence      = Capped(lowConfidence),
                stale              = Capped(stale)
            };

            return ToolResult.Ok(call.Id, Definition.Name,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "analyze_coverage failed");
            return ToolResult.Fail(call.Id, Definition.Name, ex.Message);
        }
    }

    /// <summary>Yields each relation kind and its target rule IDs for a rule's relations.</summary>
    private static IEnumerable<(string Relation, IReadOnlyList<string> Targets)> EnumerateRelations(
        RuleRelations rel)
    {
        yield return ("implies", rel.Implies);
        yield return ("conflictsWith", rel.ConflictsWith);
        yield return ("exceptionFor", rel.ExceptionFor);
        yield return ("requires", rel.Requires);
        yield return ("supersedes", rel.Supersedes);
        yield return ("related", rel.Related);
    }

    /// <summary>
    /// Wraps a category list with its exact count and a truncation marker, listing at most
    /// <see cref="MaxItemsPerCategory"/> items to keep the JSON result compact.
    /// </summary>
    private static object Capped<T>(IReadOnlyList<T> items) => new
    {
        count     = items.Count,
        truncated = items.Count > MaxItemsPerCategory,
        items     = items.Take(MaxItemsPerCategory).ToList()
    };
}
