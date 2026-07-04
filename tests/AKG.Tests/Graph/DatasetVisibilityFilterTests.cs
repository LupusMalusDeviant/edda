using Edda.AKG.Graph;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// Unit tests for <see cref="DatasetVisibilityFilter"/> (ADR-0014): unrestricted visibility is a pass-through
/// (behaviour-neutral), a restricted visibility hides rules in non-visible datasets, and rules that belong to
/// no dataset always survive.
/// </summary>
public class DatasetVisibilityFilterTests
{
    private static KnowledgeRule Rule(string id)
        => new() { Id = id, Type = "Rule", Domain = "general", Priority = RulePriority.Medium, Body = "b" };

    [Theory]
    [InlineData("git:repo:file")]
    [InlineData("hand-authored")]
    public void IsVisible_Unrestricted_AlwaysTrue(string ruleId)
        => DatasetVisibilityFilter.IsVisible(DatasetVisibility.Unrestricted, ruleId).Should().BeTrue();

    [Fact]
    public void IsVisible_Restricted_RuleInVisibleDataset_True()
    {
        var visibility = DatasetVisibility.Restricted(["git:repo"]);

        DatasetVisibilityFilter.IsVisible(visibility, "git:repo:file").Should().BeTrue();
        DatasetVisibilityFilter.IsVisible(visibility, "git:repo").Should().BeTrue();
    }

    [Fact]
    public void IsVisible_Restricted_RuleInHiddenDataset_False()
    {
        var visibility = DatasetVisibility.Restricted(["git:repo"]);

        DatasetVisibilityFilter.IsVisible(visibility, "git:other:file").Should().BeFalse();
    }

    [Fact]
    public void IsVisible_Restricted_UngroupedRule_AlwaysTrue()
    {
        var visibility = DatasetVisibility.Restricted(["git:repo"]);

        DatasetVisibilityFilter.IsVisible(visibility, "hand-authored").Should().BeTrue();
    }

    [Fact]
    public void Apply_Unrestricted_ReturnsSameReference()
    {
        IReadOnlyList<KnowledgeRule> rules = [Rule("git:repo:file"), Rule("hand-authored")];

        DatasetVisibilityFilter.Apply(DatasetVisibility.Unrestricted, rules).Should().BeSameAs(rules);
    }

    [Fact]
    public void Apply_Restricted_KeepsVisibleAndUngrouped_DropsHidden()
    {
        IReadOnlyList<KnowledgeRule> rules =
        [
            Rule("git:repo:file"),   // visible dataset
            Rule("git:other:file"),  // hidden dataset
            Rule("hand-authored"),   // no dataset — always kept
        ];
        var visibility = DatasetVisibility.Restricted(["git:repo"]);

        DatasetVisibilityFilter.Apply(visibility, rules)
            .Select(r => r.Id)
            .Should().BeEquivalentTo("git:repo:file", "hand-authored");
    }
}
