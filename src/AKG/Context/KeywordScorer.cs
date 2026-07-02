using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Scores knowledge rules based on keyword overlap with the current task context. Tags and concepts are
/// matched against whole task tokens (word-boundary, lowercase) rather than substrings, and the match
/// count is log-saturated so many matches do not dominate (B1, Stufe 1). Pure logic — no external
/// dependencies.
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
        // B1: match tags/concepts against whole task tokens (not substrings), so "test" no longer
        // matches "latest". Tokenisation is word-boundary based and lowercase.
        var taskTokens = Tokenize(context.Task.ToLowerInvariant());
        var conceptSet = context.Concepts
            .Select(c => c.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var result = new List<ScoredRule>(rules.Count);
        foreach (var rule in rules)
        {
            var score = CalculateScore(rule, taskTokens, conceptSet, activeDomains);
            result.Add(new ScoredRule { Rule = rule, Score = score });
        }

        result.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return result;
    }

    private static double CalculateScore(
        KnowledgeRule rule,
        IReadOnlySet<string> taskTokens,
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
            if (MatchesWholeTokens(lower, taskTokens))
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
                else if (MatchesWholeTokens(lower, taskTokens))
                    matchCount++;
            }
        }

        // Domain routing: rules in a domain that is active for this task get a relevance bonus,
        // so domain-relevant knowledge surfaces even without direct keyword overlap.
        if (activeDomains is { Count: > 0 } && activeDomains.Contains(rule.Domain))
            matchCount += DomainMatchBonus;

        // B1: log saturation so many matches do not dominate; Log(1+0)=0 keeps a no-match score at 0,
        // and the priority ratio is preserved (the log factor cancels for equal matchCount).
        return priorityMultiplier * Math.Log(1.0 + matchCount);
    }

    /// <summary>
    /// Splits already-lowercased text into whole tokens on non-alphanumeric boundaries (Unicode-aware via
    /// <see cref="char.IsLetterOrDigit(char)"/>), discarding empty runs. Enables whole-token matching so a
    /// tag is not matched as a substring of a larger word.
    /// </summary>
    /// <param name="lowerText">The lowercase text to tokenise.</param>
    /// <returns>The distinct tokens.</returns>
    private static HashSet<string> Tokenize(string lowerText)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i < lowerText.Length; i++)
        {
            if (char.IsLetterOrDigit(lowerText[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                tokens.Add(lowerText[start..i]);
                start = -1;
            }
        }

        if (start >= 0)
            tokens.Add(lowerText[start..]);

        return tokens;
    }

    /// <summary>
    /// Whether every token of <paramref name="phraseLower"/> is present in <paramref name="taskTokens"/>
    /// (and the phrase has at least one token). A single-word tag matches only that exact task token; a
    /// multi-word or hyphenated tag matches only when all of its tokens occur.
    /// </summary>
    /// <param name="phraseLower">The lowercase tag/concept to test.</param>
    /// <param name="taskTokens">The task's token set.</param>
    /// <returns><see langword="true"/> when the phrase matches on whole tokens.</returns>
    private static bool MatchesWholeTokens(string phraseLower, IReadOnlySet<string> taskTokens)
    {
        var phraseTokens = Tokenize(phraseLower);
        return phraseTokens.Count > 0 && phraseTokens.All(taskTokens.Contains);
    }
}
