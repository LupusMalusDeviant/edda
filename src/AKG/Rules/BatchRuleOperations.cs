using Edda.Core.Models;

namespace Edda.AKG.Rules;

/// <summary>Pure application of a single batch operation to one rule (E8). No I/O — trivially testable.</summary>
internal static class BatchRuleOperations
{
    /// <summary>
    /// Applies <paramref name="op"/> to <paramref name="rule"/>. Returns the modified rule, or <c>null</c> when
    /// the operation is a no-op for this rule (already tagged / not tagged / same priority / invalid value).
    /// </summary>
    /// <param name="rule">The rule to mutate.</param>
    /// <param name="op">The batch operation.</param>
    /// <returns>The modified rule, or null for a no-op.</returns>
    public static KnowledgeRule? Apply(KnowledgeRule rule, BatchRuleOperation op)
    {
        switch (op.Type)
        {
            case BatchRuleOperationType.AddTag:
                if (string.IsNullOrWhiteSpace(op.Tag) || rule.Tags.Contains(op.Tag, StringComparer.OrdinalIgnoreCase))
                    return null;
                return rule with { Tags = [.. rule.Tags, op.Tag] };

            case BatchRuleOperationType.RemoveTag:
                if (string.IsNullOrWhiteSpace(op.Tag) || !rule.Tags.Contains(op.Tag, StringComparer.OrdinalIgnoreCase))
                    return null;
                return rule with { Tags = rule.Tags.Where(t => !string.Equals(t, op.Tag, StringComparison.OrdinalIgnoreCase)).ToList() };

            case BatchRuleOperationType.SetPriority:
                if (op.Priority is not { } p || rule.Priority == p)
                    return null;
                return rule with { Priority = p };

            default:
                return null;
        }
    }
}
