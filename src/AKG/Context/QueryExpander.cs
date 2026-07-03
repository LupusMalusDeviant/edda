using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Deterministic query expansion via concept co-occurrence over the curated knowledge (B5): rules
/// whose tags/concepts overlap the query contribute their remaining tags/concepts as related terms.
/// Pure logic — no LLM, no external word lists; the knowledge base itself is the thesaurus.
/// </summary>
internal static class QueryExpander
{
    /// <summary>
    /// Returns up to <paramref name="maxTerms"/> related terms that the query does not already match,
    /// ranked by how many query-overlapping rules mention them (ties broken ordinally for determinism).
    /// </summary>
    /// <param name="queryTerms">Lowercased query tokens plus extracted query concepts.</param>
    /// <param name="rules">The candidate rules whose tags/concepts form the co-occurrence source.</param>
    /// <param name="maxTerms">Maximum number of expansion terms (0 disables expansion).</param>
    /// <returns>The expansion terms (lowercase), or an empty set.</returns>
    internal static IReadOnlySet<string> Expand(
        IReadOnlySet<string> queryTerms,
        IReadOnlyList<KnowledgeRule> rules,
        int maxTerms)
    {
        if (maxTerms <= 0 || queryTerms.Count == 0 || rules.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var terms = RuleTerms(rule);
            if (terms.Count == 0)
                continue;

            // The rule contributes only when at least one of its terms matches the query.
            if (!terms.Any(t => KeywordScorer.MatchesWholeTokens(t, queryTerms)))
                continue;

            foreach (var term in terms)
            {
                // Terms the query already matches are not expansion candidates.
                if (KeywordScorer.MatchesWholeTokens(term, queryTerms))
                    continue;
                counts[term] = counts.GetValueOrDefault(term) + 1;
            }
        }

        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(maxTerms)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Lowercased tags plus trigger concepts of a rule (its co-occurrence vocabulary).</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The distinct lowercase terms.</returns>
    private static HashSet<string> RuleTerms(KnowledgeRule rule)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in rule.Tags)
            terms.Add(tag.ToLowerInvariant());
        if (rule.WhenRelevant is not null)
            foreach (var concept in rule.WhenRelevant.DetectedConcepts)
                terms.Add(concept.ToLowerInvariant());
        return terms;
    }
}
