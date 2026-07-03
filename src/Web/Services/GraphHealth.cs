using Edda.Core.Models;

namespace Edda.Web.Services;

/// <summary>A thinly-covered domain: a domain with at most <see cref="GraphHealth.ThinDomainMaxRules"/> rules.</summary>
public sealed record ThinDomain(string Domain, int RuleCount);

/// <summary>A rule whose confidence multiplier is below the low-confidence threshold.</summary>
public sealed record LowConfidenceRule(string RuleId, double Multiplier);

/// <summary>A rule whose most recent feedback is older than the staleness window.</summary>
public sealed record StaleRule(string RuleId, int AgeDays);

/// <summary>Counts of rules by confidence band (below 0.7 / 0.7–1.0 / above 1.0).</summary>
public sealed record ConfidenceBuckets(int Low, int Neutral, int Boosted);

/// <summary>Structured graph-health summary for the /quality dashboard (E5).</summary>
public sealed record GraphHealthReport(
    int TotalRules,
    int DomainCount,
    IReadOnlyList<ThinDomain> ThinDomains,
    IReadOnlyList<LowConfidenceRule> LowConfidence,
    IReadOnlyList<StaleRule> Stale,
    int ConflictCount,
    int DanglingReferenceCount,
    ConfidenceBuckets Confidence);

/// <summary>
/// Pure, deterministic graph-health analysis for the /quality dashboard (E5): thin domains, unresolved
/// conflicts, dangling references, low-confidence and stale rules, and the confidence distribution. Mirrors
/// the analyze_coverage thresholds; kept separate from that tool to avoid touching its JSON contract.
/// </summary>
public static class GraphHealth
{
    /// <summary>A domain with at most this many rules is thinly covered.</summary>
    public const int ThinDomainMaxRules = 2;

    /// <summary>Rules whose confidence multiplier is below this are low-confidence.</summary>
    public const double LowConfidenceThreshold = 0.7;

    /// <summary>Analyzes the graph's health from already-fetched rules and feedback stats.</summary>
    /// <param name="rules">All rules in scope.</param>
    /// <param name="stats">Feedback statistics (from <c>GetAllStatsAsync</c>).</param>
    /// <param name="now">Current time (from the injected <see cref="TimeProvider"/>).</param>
    /// <param name="staleDays">A rule is stale when its last feedback is older than this many days.</param>
    /// <returns>The structured health report.</returns>
    public static GraphHealthReport Analyze(
        IReadOnlyList<KnowledgeRule> rules,
        IReadOnlyList<RuleFeedbackStats> stats,
        DateTimeOffset now,
        int staleDays)
    {
        var ruleIds = rules.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var thin = rules
            .GroupBy(r => r.Domain, StringComparer.Ordinal)
            .Where(g => g.Count() <= ThinDomainMaxRules)
            .Select(g => new ThinDomain(g.Key, g.Count()))
            .OrderBy(d => d.RuleCount).ThenBy(d => d.Domain, StringComparer.Ordinal)
            .ToList();

        var conflictPairs = new HashSet<string>(StringComparer.Ordinal);
        var dangling = 0;
        foreach (var r in rules)
        {
            if (r.RelatesTo is not { } rel) continue;
            foreach (var targets in Relations(rel))
                foreach (var t in targets)
                    if (!ruleIds.Contains(t)) dangling++;
            foreach (var t in rel.ConflictsWith)
                if (ruleIds.Contains(t))
                    conflictPairs.Add(string.CompareOrdinal(r.Id, t) <= 0 ? $"{r.Id}|{t}" : $"{t}|{r.Id}");
        }

        var scoped = stats.Where(s => ruleIds.Contains(s.RuleId)).ToList();

        var low = scoped
            .Where(s => s.ConfidenceMultiplier < LowConfidenceThreshold)
            .OrderBy(s => s.ConfidenceMultiplier)
            .Select(s => new LowConfidenceRule(s.RuleId, Math.Round(s.ConfidenceMultiplier, 2)))
            .ToList();

        var stale = scoped
            .Where(s => s.LastFeedbackAt is { } l && (now - l).TotalDays > staleDays)
            .Select(s => new StaleRule(s.RuleId, (int)(now - s.LastFeedbackAt!.Value).TotalDays))
            .OrderByDescending(x => x.AgeDays)
            .ToList();

        var buckets = new ConfidenceBuckets(
            Low:     scoped.Count(s => s.ConfidenceMultiplier < LowConfidenceThreshold),
            Neutral: scoped.Count(s => s.ConfidenceMultiplier is >= LowConfidenceThreshold and <= 1.0),
            Boosted: scoped.Count(s => s.ConfidenceMultiplier > 1.0));

        return new GraphHealthReport(
            rules.Count,
            rules.Select(r => r.Domain).Distinct(StringComparer.Ordinal).Count(),
            thin, low, stale, conflictPairs.Count, dangling, buckets);
    }

    private static IEnumerable<IReadOnlyList<string>> Relations(RuleRelations rel)
    {
        yield return rel.Implies;
        yield return rel.ConflictsWith;
        yield return rel.ExceptionFor;
        yield return rel.Requires;
        yield return rel.Supersedes;
        yield return rel.Related;
    }
}
