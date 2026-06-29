namespace Edda.Core.Models;

/// <summary>
/// Result of a TDK validation pass over an agent response.
/// When <see cref="HasViolations"/> is <see langword="true"/> the pipeline re-queries the model
/// with targeted feedback built from <see cref="Violations"/>.
/// </summary>
public sealed record TdkResult
{
    /// <summary><see langword="true"/> when at least one rule violation was detected.</summary>
    public required bool HasViolations { get; init; }

    /// <summary>Detailed violation records. Empty when <see cref="HasViolations"/> is <see langword="false"/>.</summary>
    public IReadOnlyList<TdkViolation> Violations { get; init; } = [];

    /// <summary>
    /// Singleton representing a clean validation with no violations.
    /// Use instead of allocating a new instance for the common case.
    /// </summary>
    public static TdkResult NoViolations { get; } =
        new() { HasViolations = false, Violations = [] };
}

/// <summary>
/// A single rule violation detected during TDK validation.
/// </summary>
/// <param name="RuleId">The AKG rule that was violated.</param>
/// <param name="Message">Human-readable description of how the response violates the rule.</param>
/// <param name="Severity">Severity level string (e.g. "critical", "high", "medium", "low").</param>
public sealed record TdkViolation(string RuleId, string Message, string Severity);
