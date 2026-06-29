namespace Edda.Core.Models;

/// <summary>
/// Result of a code review across all agent branches for an ASPS project.
/// Contains findings, automated check results, and approval status.
/// </summary>
/// <param name="Approved">True if no critical findings remain and all checks pass.</param>
/// <param name="TotalFindings">Total number of findings across all agents.</param>
/// <param name="CriticalFindings">Number of findings with severity "critical".</param>
/// <param name="Findings">All review findings grouped by agent/task.</param>
/// <param name="BuildResult">Result of the build verification check. Null if not executed.</param>
/// <param name="TestResult">Result of the test verification check. Null if not executed.</param>
/// <param name="ReviewedAt">Timestamp when the review completed.</param>
public sealed record ReviewResult(
    bool Approved,
    int TotalFindings,
    int CriticalFindings,
    IReadOnlyList<ReviewFinding> Findings,
    ReviewVerificationResult? BuildResult,
    ReviewVerificationResult? TestResult,
    DateTimeOffset ReviewedAt);

/// <summary>
/// A single finding from the code review, associated with a specific file and agent.
/// </summary>
/// <param name="FindingId">Unique identifier (e.g. "REV-001").</param>
/// <param name="TaskId">Which agent/task is affected.</param>
/// <param name="Severity">Finding severity: "critical", "major", "minor", or "info".</param>
/// <param name="Category">Finding category: "spec_mismatch", "api_incompatible", "security", "quality", or "test_missing".</param>
/// <param name="FilePath">Path to the affected file.</param>
/// <param name="LineNumber">Line number in the file, if applicable.</param>
/// <param name="Description">Human-readable description of the issue.</param>
/// <param name="SuggestedFix">Optional suggestion for how to fix the issue.</param>
public sealed record ReviewFinding(
    string FindingId,
    string TaskId,
    string Severity,
    string Category,
    string FilePath,
    int? LineNumber,
    string Description,
    string? SuggestedFix);

/// <summary>
/// Result of a single fix round where agents attempt to resolve review findings.
/// </summary>
/// <param name="FixesRequested">Number of fixes dispatched to agents.</param>
/// <param name="FixesCompleted">Number of fixes successfully resolved.</param>
/// <param name="FixesFailed">Number of fixes that could not be resolved.</param>
/// <param name="RemainingIssues">Descriptions of issues that still remain after the fix round.</param>
public sealed record FixRoundResult(
    int FixesRequested,
    int FixesCompleted,
    int FixesFailed,
    IReadOnlyList<string> RemainingIssues);

/// <summary>
/// Current status of the review process for a project.
/// </summary>
/// <param name="ProjectId">The internal project ID.</param>
/// <param name="State">Review state: "pending", "reviewing", "fix_round", "approved", or "rejected".</param>
/// <param name="ReviewRound">Current review round (1-based). Increments with each fix cycle.</param>
/// <param name="TotalFindings">Total findings in the most recent review.</param>
/// <param name="ResolvedFindings">Findings resolved so far across all fix rounds.</param>
public sealed record ReviewStatus(
    string ProjectId,
    string State,
    int ReviewRound,
    int TotalFindings,
    int ResolvedFindings);

/// <summary>
/// Result of running a verification command (build, test, lint) during code review.
/// Differs from <see cref="DevVerificationResult"/> by using a combined output field
/// and an explicit errors list for structured parsing.
/// </summary>
/// <param name="Success">True if the command succeeded (exit code 0).</param>
/// <param name="Command">The command that was executed.</param>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="Output">Combined stdout/stderr output.</param>
/// <param name="Errors">Extracted error messages from the output.</param>
public sealed record ReviewVerificationResult(
    bool Success,
    string Command,
    int ExitCode,
    string Output,
    IReadOnlyList<string> Errors);
