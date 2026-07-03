using Edda.Core.Models;
using Edda.Web.Services;

namespace Edda.Web.Tests;

/// <summary>Unit tests for <see cref="GraphHealth"/> (E5 /quality dashboard analysis).</summary>
public sealed class GraphHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static KnowledgeRule Rule(string id, string domain = "coding", RuleRelations? relatesTo = null)
        => new() { Id = id, Type = "Rule", Domain = domain, Priority = RulePriority.Medium, Body = "body", RelatesTo = relatesTo };

    private static RuleFeedbackStats Stats(string ruleId, double multiplier = 1.0, DateTimeOffset? lastFeedbackAt = null)
        => new() { RuleId = ruleId, ConfidenceMultiplier = multiplier, LastFeedbackAt = lastFeedbackAt };

    [Fact]
    public void Analyze_EmptyGraph_ReturnsZeros()
    {
        var r = GraphHealth.Analyze([], [], Now, 90);

        r.TotalRules.Should().Be(0);
        r.DomainCount.Should().Be(0);
        r.ThinDomains.Should().BeEmpty();
        r.ConflictCount.Should().Be(0);
        r.DanglingReferenceCount.Should().Be(0);
        r.Confidence.Should().Be(new ConfidenceBuckets(0, 0, 0));
    }

    [Fact]
    public void Analyze_TotalsAndDomainCount_AreCorrect()
    {
        var rules = new[] { Rule("a", "d1"), Rule("b", "d1"), Rule("c", "d2") };

        var r = GraphHealth.Analyze(rules, [], Now, 90);

        r.TotalRules.Should().Be(3);
        r.DomainCount.Should().Be(2);
    }

    [Fact]
    public void Analyze_ThinDomains_FlagsDomainsWithAtMostTwoRules()
    {
        var rules = new[]
        {
            Rule("t1", "d-thin"), Rule("t2", "d-thin"),
            Rule("f1", "d-fat"), Rule("f2", "d-fat"), Rule("f3", "d-fat"),
        };

        var r = GraphHealth.Analyze(rules, [], Now, 90);

        r.ThinDomains.Should().ContainSingle().Which.Domain.Should().Be("d-thin");
        r.ThinDomains[0].RuleCount.Should().Be(2);
    }

    [Fact]
    public void Analyze_Conflicts_CountsDedupedPairs()
    {
        var rules = new[]
        {
            Rule("a", relatesTo: new RuleRelations { ConflictsWith = ["b"] }),
            Rule("b", relatesTo: new RuleRelations { ConflictsWith = ["a"] }),   // reverse edge — same pair
        };

        GraphHealth.Analyze(rules, [], Now, 90).ConflictCount.Should().Be(1);
    }

    [Fact]
    public void Analyze_DanglingReferences_CountsMissingTargets()
    {
        var rules = new[]
        {
            Rule("a", relatesTo: new RuleRelations { Implies = ["ghost1", "ghost2"], Requires = ["b"] }),
            Rule("b"),
        };

        GraphHealth.Analyze(rules, [], Now, 90).DanglingReferenceCount.Should().Be(2);
    }

    [Fact]
    public void Analyze_LowConfidence_FlagsBelowThreshold()
    {
        var rules = new[] { Rule("a"), Rule("b") };
        var stats = new[] { Stats("a", 0.6), Stats("b", 0.7) };   // 0.6 < 0.7 flagged; 0.7 not

        var r = GraphHealth.Analyze(rules, stats, Now, 90);

        r.LowConfidence.Should().ContainSingle().Which.RuleId.Should().Be("a");
    }

    [Fact]
    public void Analyze_Stale_FlagsFeedbackOlderThanWindow()
    {
        var rules = new[] { Rule("a"), Rule("b") };
        var stats = new[]
        {
            Stats("a", lastFeedbackAt: Now.AddDays(-100)),   // 100 > 90 → stale
            Stats("b", lastFeedbackAt: Now.AddDays(-10)),    // 10 → fresh
        };

        var r = GraphHealth.Analyze(rules, stats, Now, 90);

        r.Stale.Should().ContainSingle().Which.RuleId.Should().Be("a");
        r.Stale[0].AgeDays.Should().Be(100);
    }

    [Fact]
    public void Analyze_ConfidenceBuckets_PartitionByMultiplier()
    {
        var rules = new[] { Rule("a"), Rule("b"), Rule("c") };
        var stats = new[] { Stats("a", 0.5), Stats("b", 0.9), Stats("c", 1.2) };

        GraphHealth.Analyze(rules, stats, Now, 90).Confidence
            .Should().Be(new ConfidenceBuckets(Low: 1, Neutral: 1, Boosted: 1));
    }
}
