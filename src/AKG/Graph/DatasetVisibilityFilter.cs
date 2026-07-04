using Edda.Core.Models;

namespace Edda.AKG.Graph;

/// <summary>
/// Applies a resolved <see cref="DatasetVisibility"/> to graph rule reads (ADR-0014). Under
/// <see cref="DatasetVisibility.IsUnrestricted"/> the input passes through unchanged — the behaviour-neutral
/// default that keeps the Cypher and its results byte-identical to the pre-dataset behaviour. Otherwise a rule
/// survives only when it belongs to no dataset or to a visible one; dataset membership is derived from the rule
/// id via <see cref="DatasetMembership"/>.
/// </summary>
internal static class DatasetVisibilityFilter
{
    /// <summary>Whether a single rule id is readable under the given visibility.</summary>
    /// <param name="visibility">The caller's resolved dataset visibility.</param>
    /// <param name="ruleId">The rule id to test.</param>
    /// <returns><see langword="true"/> when the rule is visible.</returns>
    public static bool IsVisible(DatasetVisibility visibility, string ruleId)
    {
        if (visibility.IsUnrestricted) return true;
        var datasetId = DatasetMembership.DatasetIdOf(ruleId);
        return datasetId is null || visibility.VisibleDatasetIds.Contains(datasetId);
    }

    /// <summary>
    /// Filters a rule list to those readable under the given visibility. Returns the input reference unchanged
    /// when <paramref name="visibility"/> is unrestricted, so the default read path allocates nothing extra.
    /// </summary>
    /// <param name="visibility">The caller's resolved dataset visibility.</param>
    /// <param name="rules">The rules to filter.</param>
    /// <returns>The visible rules.</returns>
    public static IReadOnlyList<KnowledgeRule> Apply(DatasetVisibility visibility, IReadOnlyList<KnowledgeRule> rules)
    {
        if (visibility.IsUnrestricted) return rules;
        return rules.Where(r => IsVisible(visibility, r.Id)).ToList();
    }
}
