namespace Edda.Core.Models;

/// <summary>
/// Result of a startup validation run, containing all detected issues.
/// </summary>
/// <param name="IsValid">True if no errors were found (warnings and infos are acceptable).</param>
/// <param name="Issues">All detected validation issues, ordered by severity descending.</param>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// A single issue detected during startup validation.
/// </summary>
/// <param name="Severity">How critical this issue is.</param>
/// <param name="Component">The subsystem or component where the issue was detected.</param>
/// <param name="Message">Human-readable description of the issue.</param>
/// <param name="Suggestion">Optional action the operator can take to resolve the issue.</param>
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Component,
    string Message,
    string? Suggestion = null);

/// <summary>Severity levels for startup validation issues.</summary>
public enum ValidationSeverity
{
    /// <summary>The system cannot start or will malfunction. Operator action required.</summary>
    Error,

    /// <summary>Non-critical issue that may degrade functionality.</summary>
    Warning,

    /// <summary>Informational message, no action required.</summary>
    Info
}
