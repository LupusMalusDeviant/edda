using Edda.AKG.Context;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Context;

/// <summary>B5: <see cref="QueryExpander"/> — deterministic co-occurrence expansion.</summary>
public class QueryExpanderTests
{
    private static KnowledgeRule Rule(
        string id, IReadOnlyList<string>? tags = null, IReadOnlyList<string>? concepts = null) => new()
    {
        Id = id, Type = "Rule", Domain = "general", Priority = RulePriority.Medium, Body = "b",
        Tags = tags ?? [],
        WhenRelevant = concepts is { Count: > 0 } ? new WhenRelevant { DetectedConcepts = concepts } : null,
    };

    private static IReadOnlySet<string> Query(params string[] terms)
        => terms.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Expand_SharedTerm_ReturnsCoOccurringTerms()
    {
        var rules = new[] { Rule("r1", tags: ["kafka"], concepts: ["queue", "broker"]) };

        var expanded = QueryExpander.Expand(Query("kafka"), rules, maxTerms: 5);

        expanded.Should().BeEquivalentTo(["queue", "broker"]);
    }

    [Fact]
    public void Expand_ExcludesTermsAlreadyInQuery()
    {
        var rules = new[] { Rule("r1", tags: ["kafka", "queue"], concepts: ["broker"]) };

        var expanded = QueryExpander.Expand(Query("kafka", "queue"), rules, maxTerms: 5);

        expanded.Should().BeEquivalentTo(["broker"]);
    }

    [Fact]
    public void Expand_RespectsMaxTerms_WithDeterministicOrder()
    {
        // "shared" appears in both rules (count 2) and wins; the tie between "alpha" and "beta"
        // (count 1 each) is broken ordinally, so "alpha" takes the second slot.
        var rules = new[]
        {
            Rule("r1", tags: ["kafka"], concepts: ["shared", "beta"]),
            Rule("r2", tags: ["kafka"], concepts: ["shared", "alpha"]),
        };

        var expanded = QueryExpander.Expand(Query("kafka"), rules, maxTerms: 2);

        expanded.Should().BeEquivalentTo(["shared", "alpha"]);
    }

    [Fact]
    public void Expand_NoOverlap_ReturnsEmpty()
    {
        var rules = new[] { Rule("r1", tags: ["kafka"], concepts: ["queue"]) };

        var expanded = QueryExpander.Expand(Query("unrelated"), rules, maxTerms: 5);

        expanded.Should().BeEmpty();
    }

    [Fact]
    public void Expand_MaxTermsZero_ReturnsEmpty()
    {
        var rules = new[] { Rule("r1", tags: ["kafka"], concepts: ["queue"]) };

        var expanded = QueryExpander.Expand(Query("kafka"), rules, maxTerms: 0);

        expanded.Should().BeEmpty();
    }

    [Fact]
    public void Expand_MultiWordTerm_MatchesOnWholeTokens()
    {
        // The rule's multi-word tag "message broker" matches the query tokens {message, broker},
        // so its concepts co-occur; the multi-word tag itself is not an expansion candidate
        // because the query already matches it.
        var rules = new[] { Rule("r1", tags: ["message broker"], concepts: ["kafka"]) };

        var expanded = QueryExpander.Expand(Query("message", "broker"), rules, maxTerms: 5);

        expanded.Should().BeEquivalentTo(["kafka"]);
    }
}
