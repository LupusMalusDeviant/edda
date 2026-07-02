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
    /// Engine/infrastructure failures encountered while running validators (sandbox crash, timeout,
    /// non-zero exit, or invalid validator output). These are surfaced to the caller but are NOT counted
    /// as rule pass/fail outcomes, so an infrastructure problem never skews a rule's confidence.
    /// </summary>
    public IReadOnlyList<TdkEngineError> EngineErrors { get; init; } = [];

    /// <summary><see langword="true"/> when at least one validator could not be executed.</summary>
    public bool HasEngineErrors => EngineErrors.Count > 0;

    /// <summary>
    /// Singleton representing a clean validation with no violations and no engine errors.
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
/// <param name="Line">Optional 1-based source line in the validated code where the violation was found.</param>
/// <param name="Suggestion">Optional suggested fix for the violation.</param>
public sealed record TdkViolation(
    string RuleId,
    string Message,
    string Severity,
    int? Line = null,
    string? Suggestion = null);

/// <summary>
/// An engine/infrastructure failure while running a validator (sandbox crash, timeout, non-zero exit,
/// or invalid validator output) — as opposed to a rule violation. Surfaced to the caller so a failed
/// validator is visible rather than silently ignored, but deliberately not booked as a pass/fail
/// outcome, since an infrastructure error is not a business result.
/// </summary>
/// <param name="RuleId">The rule whose validator failed to run.</param>
/// <param name="Reason">Short description of what went wrong.</param>
/// <param name="ExitCode">Validator process exit code, when available.</param>
/// <param name="Stderr">A short excerpt of the validator's standard error, when available.</param>
/// <param name="TimedOut"><see langword="true"/> when the validator exceeded its time budget.</param>
public sealed record TdkEngineError(
    string RuleId,
    string Reason,
    int? ExitCode = null,
    string? Stderr = null,
    bool TimedOut = false);
