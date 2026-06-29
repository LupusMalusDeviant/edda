using Edda.AKG.Context;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Context;

public class KeywordScorerTests
{
    private readonly KeywordScorer _scorer = new();

    private static KnowledgeRule MakeRule(
        string id,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? detectedConcepts = null,
        RulePriority priority = RulePriority.Medium)
    {
        return new KnowledgeRule
        {
            Id = id,
            Type = "Rule",
            Domain = "general",
            Priority = priority,
            Body = "body",
            Tags = tags ?? [],
            WhenRelevant = detectedConcepts is { Count: > 0 }
                ? new WhenRelevant { DetectedConcepts = detectedConcepts }
                : null,
        };
    }

    private static TaskContext MakeContext(string task, IReadOnlyList<string>? concepts = null)
        => new() { Task = task, Concepts = concepts ?? [] };

    [Fact]
    public void Score_MatchingTagInTask_ReturnsPositiveScore()
    {
        var rule = MakeRule("async-rule", tags: ["async"]);
        var context = MakeContext("Use async await pattern");

        var results = _scorer.Score([rule], context);

        results.Should().HaveCount(1);
        results[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Score_MatchingTagInConcepts_BoostsScore()
    {
        // Tag matches task text only: score = 2 * 1 = 2 (Medium)
        var ruleTaskOnly = MakeRule("task-only", tags: ["async"]);
        var contextTaskOnly = MakeContext("Use async await", concepts: []);

        // Tag matches both task text (+1) AND concepts (+2): score = 2 * 3 = 6 (Medium)
        var ruleConcept = MakeRule("with-concept", tags: ["async"]);
        var contextWithConcept = MakeContext("Use async await", concepts: ["async"]);

        var scoreTaskOnly = _scorer.Score([ruleTaskOnly], contextTaskOnly)[0].Score;
        var scoreWithConcept = _scorer.Score([ruleConcept], contextWithConcept)[0].Score;

        scoreWithConcept.Should().BeGreaterThan(scoreTaskOnly,
            because: "concept match (+2) should boost higher than task-text match alone (+1)");
    }

    [Fact]
    public void Score_WhenRelevantConcepts_BoostsHighest()
    {
        // Tag rule: task text only match (+1 per tag), score = 2 * 1 = 2
        // (tag "docker" is in task text but NOT in concepts)
        var ruleWithTag = MakeRule("tag-rule", tags: ["docker"]);

        // WhenRelevant rule: concept match (+3), score = 2 * 3 = 6
        var ruleWithConcept = MakeRule("concept-rule", detectedConcepts: ["async"]);

        // Task mentions docker (so tag rule matches task text), async is a concept (so WhenRelevant matches)
        var context = MakeContext("Use docker containers", concepts: ["async"]);

        var tagScore = _scorer.Score([ruleWithTag], context)[0].Score;
        var conceptScore = _scorer.Score([ruleWithConcept], context)[0].Score;

        conceptScore.Should().BeGreaterThan(tagScore,
            because: "WhenRelevant.DetectedConcepts concept match (+3) outscores tag task-text match (+1)");
    }

    [Fact]
    public void Score_NoMatch_ReturnsZeroScore()
    {
        var rule = MakeRule("unrelated-rule", tags: ["docker", "kubernetes"]);
        var context = MakeContext("Write a unit test in csharp");

        var results = _scorer.Score([rule], context);

        results[0].Score.Should().Be(0);
    }

    [Fact]
    public void Score_CriticalPriority_MultiplierApplied()
    {
        var mediumRule = MakeRule("medium-rule", tags: ["async"], priority: RulePriority.Medium);
        var criticalRule = MakeRule("critical-rule", tags: ["async"], priority: RulePriority.Critical);
        var context = MakeContext("async operation");

        var mediumScore = _scorer.Score([mediumRule], context)[0].Score;
        var criticalScore = _scorer.Score([criticalRule], context)[0].Score;

        criticalScore.Should().BeGreaterThan(mediumScore,
            because: "Critical priority multiplier (4x) exceeds Medium (2x)");
        (criticalScore / mediumScore).Should().BeApproximately(2.0, 0.001,
            because: "Critical (4x) / Medium (2x) = 2.0 ratio");
    }

    [Fact]
    public void Score_ResultsSortedDescending()
    {
        var lowRule = MakeRule("low-rule", tags: ["async"], priority: RulePriority.Low);
        var highRule = MakeRule("high-rule", tags: ["async"], priority: RulePriority.High);
        var criticalRule = MakeRule("critical-rule", tags: ["async"], priority: RulePriority.Critical);
        var context = MakeContext("async await");

        var results = _scorer.Score([lowRule, criticalRule, highRule], context);

        results.Select(r => r.Rule.Id).Should().Equal("critical-rule", "high-rule", "low-rule");
        results.Select(r => r.Score).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Score_RuleInActiveDomain_SurfacesDespiteNoKeywordMatch()
    {
        // Rule has no keyword overlap with the task → keyword score is 0.
        var rule = MakeRule("domain-rule");   // Domain = "general"
        var context = MakeContext("completely unrelated request");
        var activeDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "general" };

        var withoutDomain = _scorer.Score([rule], context)[0].Score;
        var withDomain = _scorer.Score([rule], context, activeDomains)[0].Score;

        withoutDomain.Should().Be(0);
        withDomain.Should().BeGreaterThan(0,
            because: "an active domain adds a relevance bonus even without keyword overlap");
    }

    [Fact]
    public void Score_RuleNotInActiveDomain_ReceivesNoBonus()
    {
        var rule = MakeRule("rule");   // Domain = "general"
        var context = MakeContext("unrelated");
        var activeDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "security" };

        var score = _scorer.Score([rule], context, activeDomains)[0].Score;

        score.Should().Be(0);
    }

    [Fact]
    public void Score_ActiveDomainMatch_IsCaseInsensitive()
    {
        var rule = MakeRule("rule");   // Domain = "general"
        var context = MakeContext("unrelated");
        var activeDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GENERAL" };

        var score = _scorer.Score([rule], context, activeDomains)[0].Score;

        score.Should().BeGreaterThan(0);
    }
}
