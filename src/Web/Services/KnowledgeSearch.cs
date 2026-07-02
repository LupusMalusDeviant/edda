using Edda.Web.Components.AKG;

namespace Edda.Web.Services;

/// <summary>
/// Pure, deterministic filtering of loaded knowledge-graph head nodes for the /knowledge search box (E1).
/// Free-text matches the rule id, body or domain (case-insensitive substring); the domain/type/tag arguments
/// are exact filters (null/blank = no filter). No I/O — trivially unit-testable.
/// </summary>
public static class KnowledgeSearch
{
    /// <summary>Filters <paramref name="rules"/> by free-text query and exact domain/type/tag filters.</summary>
    /// <param name="rules">The loaded head rules.</param>
    /// <param name="query">Free-text; matched against id/body/domain (case-insensitive). Null/blank = no text filter.</param>
    /// <param name="domain">Exact domain filter, or null/blank for all.</param>
    /// <param name="type">Exact type filter, or null/blank for all.</param>
    /// <param name="tag">Tag that a rule must carry, or null/blank for all.</param>
    /// <returns>The matching rules, order preserved.</returns>
    public static IReadOnlyList<KnowledgeGraphView.KnowledgeRuleDto> Filter(
        IReadOnlyList<KnowledgeGraphView.KnowledgeRuleDto> rules,
        string? query,
        string? domain,
        string? type,
        string? tag)
    {
        IEnumerable<KnowledgeGraphView.KnowledgeRuleDto> result = rules;

        if (!string.IsNullOrWhiteSpace(domain))
            result = result.Where(r => string.Equals(r.Domain, domain, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(type))
            result = result.Where(r => string.Equals(r.Type, type, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(tag))
            result = result.Where(r => r.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            result = result.Where(r =>
                Contains(r.Id, q) || Contains(r.Body, q) || Contains(r.Domain, q));
        }

        return result.ToList();
    }

    private static bool Contains(string? value, string q)
        => value is not null && value.Contains(q, StringComparison.OrdinalIgnoreCase);
}
