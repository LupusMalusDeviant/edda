using Edda.AKG.Context;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Context;

/// <summary>
/// Unit tests for the context-hygiene helpers of <see cref="ContextCompiler"/>:
/// contradiction suppression (SUPERSEDES / CONFLICTS_WITH) and the character budget,
/// including the protection of pinned rules.
/// </summary>
public class ContextHygieneTests
{
    private static KnowledgeRule Rule(
        string id,
        RulePriority priority = RulePriority.Medium,
        string? body = null,
        IReadOnlyList<string>? supersedes = null,
        IReadOnlyList<string>? conflictsWith = null)
        => new()
        {
            Id = id,
            Type = "Rule",
            Domain = "general",
            Priority = priority,
            Body = body ?? "body",
            RelatesTo = supersedes is null && conflictsWith is null
                ? null
                : new RuleRelations
                {
                    Supersedes = supersedes ?? [],
                    ConflictsWith = conflictsWith ?? [],
                },
        };

    private static IReadOnlySet<string> Protect(params string[] ids)
        => new HashSet<string>(ids, StringComparer.Ordinal);

    private static IReadOnlySet<string> None() => new HashSet<string>(StringComparer.Ordinal);

    // ── Suppression ─────────────────────────────────────────────────────────────

    [Fact]
    public void SuppressContradictions_Supersedes_DropsSupersededRule()
    {
        var rules = new[]
        {
            Rule("new-rule", supersedes: ["old-rule"]),
            Rule("old-rule"),
        };

        var result = ContextCompiler.SuppressContradictions(rules, None());

        result.Select(r => r.Id).Should().Equal("new-rule");
    }

    [Fact]
    public void SuppressContradictions_ConflictLowerPriority_DropsLowerPriority()
    {
        var rules = new[]
        {
            Rule("high", RulePriority.Critical, conflictsWith: ["low"]),
            Rule("low", RulePriority.Low),
        };

        var result = ContextCompiler.SuppressContradictions(rules, None());

        result.Select(r => r.Id).Should().Equal("high");
    }

    [Fact]
    public void SuppressContradictions_ConflictEqualPriority_KeepsBoth()
    {
        var rules = new[]
        {
            Rule("a", RulePriority.Medium, conflictsWith: ["b"]),
            Rule("b", RulePriority.Medium),
        };

        var result = ContextCompiler.SuppressContradictions(rules, None());

        result.Select(r => r.Id).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void SuppressContradictions_SupersededRuleProtected_NotDropped()
    {
        var rules = new[]
        {
            Rule("new-rule", supersedes: ["pinned-old"]),
            Rule("pinned-old"),
        };

        var result = ContextCompiler.SuppressContradictions(rules, Protect("pinned-old"));

        result.Select(r => r.Id).Should().BeEquivalentTo("new-rule", "pinned-old");
    }

    [Fact]
    public void SuppressContradictions_ConflictLoserProtected_NotDropped()
    {
        var rules = new[]
        {
            Rule("high", RulePriority.Critical, conflictsWith: ["pinned-low"]),
            Rule("pinned-low", RulePriority.Low),
        };

        var result = ContextCompiler.SuppressContradictions(rules, Protect("pinned-low"));

        result.Select(r => r.Id).Should().BeEquivalentTo("high", "pinned-low");
    }

    // ── Budget ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyContextBudget_OverBudget_DropsTrailingNonProtected()
    {
        var rules = new[]
        {
            Rule("r1", body: new string('x', 100)),
            Rule("r2", body: new string('x', 100)),
            Rule("r3", body: new string('x', 100)),
        };

        // Each rule ≈ 122 chars; a 200-char budget admits only the first.
        var result = ContextCompiler.ApplyContextBudget(rules, None(), maxChars: 200);

        result.Select(r => r.Id).Should().Equal("r1");
    }

    [Fact]
    public void ApplyContextBudget_ProtectedRule_AlwaysKept()
    {
        var rules = new[]
        {
            Rule("pinned-rule", body: new string('x', 100)),
            Rule("r1", body: new string('x', 100)),
        };

        var result = ContextCompiler.ApplyContextBudget(rules, Protect("pinned-rule"), maxChars: 50);

        result.Select(r => r.Id).Should().Equal("pinned-rule");
    }

    [Fact]
    public void ApplyContextBudget_UnderBudget_KeepsAllInOrder()
    {
        var rules = new[]
        {
            Rule("r1"),
            Rule("r2"),
            Rule("r3"),
        };

        var result = ContextCompiler.ApplyContextBudget(rules, None(), maxChars: 10_000);

        result.Select(r => r.Id).Should().Equal("r1", "r2", "r3");
    }
}
