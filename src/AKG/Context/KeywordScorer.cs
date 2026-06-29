using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Scores knowledge rules based on keyword overlap with the current task context.
/// Pure logic — no external dependencies.
/// </summary>
internal sealed class KeywordScorer
{
    private const double PriorityMultiplierCritical = 4.0;
    private const double PriorityMultiplierHigh = 3.0;
    private const double PriorityMultiplierMedium = 2.0;
    private const double PriorityMultiplierLow = 1.0;

    /// <summary>Additive relevance bonus applied to the match count of a rule whose domain is active.</summary>
    private const int DomainMatchBonus = 2;

    /// <summary>
    /// Scores each rule against the task context and returns the list sorted by descending score.
    /// </summary>
    /// <param name="rules">The candidate knowledge rules to score.</param>
    /// <param name="context">The current task context providing task text and extracted concepts.</param>
    /// <param name="activeDomains">
    /// Optional set of domain names considered active for this task (see
    /// <see cref="DomainActivationResolver"/>). Rules whose <see cref="KnowledgeRule.Domain"/> is in this
    /// set receive an additive relevance bonus. When null or empty, scoring is purely keyword-based.
    /// </param>
    /// <returns>Scored rules in descending order by score.</returns>
    internal IReadOnlyList<ScoredRule> Score(
        IReadOnlyList<KnowledgeRule> rules,
        TaskContext context,
        IReadOnlySet<string>? activeDomains = null)
    {
        var taskText = context.Task.ToLowerInvariant();
        var conceptSet = context.Concepts
            .Select(c => c.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<ScoredRule>(rules.Count);
        foreach (var rule in rules)
        {
            var score = CalculateScore(rule, taskText, conceptSet, activeDomains);
            result.Add(new ScoredRule { Rule = rule, Score = score });
        }

        result.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return result;
    }

    private static double CalculateScore(
        KnowledgeRule rule,
        string taskText,
        HashSet<string> conceptSet,
        IReadOnlySet<string>? activeDomains)
    {
        var priorityMultiplier = rule.Priority switch
        {
            RulePriority.Critical => PriorityMultiplierCritical,
            RulePriority.High => PriorityMultiplierHigh,
            RulePriority.Low => PriorityMultiplierLow,
            _ => PriorityMultiplierMedium,
        };

        var matchCount = 0;

        // Tags: task-text match (+1), direct concept match (+2)
        foreach (var tag in rule.Tags)
        {
            var lower = tag.ToLowerInvariant();
            if (taskText.Contains(lower, StringComparison.Ordinal))
                matchCount++;
            if (conceptSet.Contains(lower))
                matchCount += 2;
        }

        // WhenRelevant.DetectedConcepts: direct concept match (+3), task-text match (+1)
        if (rule.WhenRelevant is not null)
        {
            foreach (var concept in rule.WhenRelevant.DetectedConcepts)
            {
                var lower = concept.ToLowerInvariant();
                if (conceptSet.Contains(lower))
                    matchCount += 3;
                else if (taskText.Contains(lower, StringComparison.Ordinal))
                    matchCount++;
            }
        }

        // Domain routing: rules in a domain that is active for this task get a relevance bonus,
        // so domain-relevant knowledge surfaces even without direct keyword overlap.
        if (activeDomains is { Count: > 0 } && activeDomains.Contains(rule.Domain))
            matchCount += DomainMatchBonus;

        return priorityMultiplier * matchCount;
    }
}
