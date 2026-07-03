namespace Edda.Core.Models;

/// <summary>A soft-deleted rule as shown in the recycle bin (E10).</summary>
public sealed record DeletedRuleInfo
{
    /// <summary>The rule id.</summary>
    public required string Id { get; init; }

    /// <summary>Short body preview (first line, truncated).</summary>
    public string BodyPreview { get; init; } = "";

    /// <summary>The rule's domain.</summary>
    public string Domain { get; init; } = "general";

    /// <summary>Owner scoping of the deleted rule (null = global).</summary>
    public string? OwnerId { get; init; }

    /// <summary>When the rule was soft-deleted.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>Who deleted the rule.</summary>
    public string? DeletedBy { get; init; }
}
