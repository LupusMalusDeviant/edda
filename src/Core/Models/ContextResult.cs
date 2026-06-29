namespace Edda.Core.Models;

/// <summary>
/// The compiled AKG context for a task. Output of the 4-phase context compilation pipeline.
/// </summary>
public sealed record ContextResult
{
    /// <summary>Rules selected as directly active for this task.</summary>
    public IReadOnlyList<KnowledgeRule> ActiveRules { get; init; } = [];

    /// <summary>Detected conflicts between active rules.</summary>
    public IReadOnlyList<RuleConflict> Conflicts { get; init; } = [];

    /// <summary>Exception relationships among active rules.</summary>
    public IReadOnlyList<RuleException> Exceptions { get; init; } = [];

    /// <summary>
    /// Pre-formatted Markdown block ready for injection into the system prompt.
    /// </summary>
    public required string FormattedContext { get; init; }

    /// <summary>An empty context result used when AKG is unavailable or compilation is skipped.</summary>
    public static ContextResult Empty => new() { FormattedContext = string.Empty };
}

/// <summary>Represents a detected semantic conflict between two rules.</summary>
/// <param name="RuleIdA">The first conflicting rule.</param>
/// <param name="RuleIdB">The second conflicting rule.</param>
/// <param name="Description">Human-readable conflict description.</param>
public sealed record RuleConflict(string RuleIdA, string RuleIdB, string Description);

/// <summary>Represents an exception relationship: RuleId is an exception for ExceptionForRuleId.</summary>
/// <param name="RuleId">The rule that acts as an exception.</param>
/// <param name="ExceptionForRuleId">The rule for which the exception applies.</param>
public sealed record RuleException(string RuleId, string ExceptionForRuleId);
