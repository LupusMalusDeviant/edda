namespace Edda.Core.Models;

/// <summary>The kind of batch operation applied to a set of AKG rules (E8).</summary>
public enum BatchRuleOperationType
{
    /// <summary>Add a tag to each rule that does not already carry it.</summary>
    AddTag,

    /// <summary>Remove a tag from each rule that carries it.</summary>
    RemoveTag,

    /// <summary>Set each rule's priority.</summary>
    SetPriority,
}

/// <summary>A single batch operation: its type plus the tag or priority it applies.</summary>
public sealed record BatchRuleOperation
{
    /// <summary>The operation type.</summary>
    public required BatchRuleOperationType Type { get; init; }

    /// <summary>The tag to add/remove (for <see cref="BatchRuleOperationType.AddTag"/>/<see cref="BatchRuleOperationType.RemoveTag"/>).</summary>
    public string? Tag { get; init; }

    /// <summary>The priority to set (for <see cref="BatchRuleOperationType.SetPriority"/>).</summary>
    public RulePriority? Priority { get; init; }
}

/// <summary>Outcome counts of a batch rule operation.</summary>
public sealed record BatchRuleResult
{
    /// <summary>Rules that were modified and upserted.</summary>
    public int Updated { get; init; }

    /// <summary>Rules skipped (not found, out of scope, or the operation was a no-op).</summary>
    public int Skipped { get; init; }

    /// <summary>Rules that failed to update.</summary>
    public int Failed { get; init; }

    /// <summary>Per-rule error messages (for the failed rules).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
