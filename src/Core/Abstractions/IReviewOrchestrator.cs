using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates code review for ASPS projects. Merges agent branches,
/// runs automated checks, performs LLM-based review against the specification,
/// and coordinates fix rounds with the original agents.
/// </summary>
public interface IReviewOrchestrator
{
    /// <summary>
    /// Runs a full code review across all agent branches.
    /// Merges branches into a review worktree, executes automated checks,
    /// and performs an LLM-based review against the Pflichtenheft and AKG rules.
    /// </summary>
    /// <param name="projectId">The internal project ID.</param>
    /// <param name="agentBranches">Branch names to review (one per agent).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Review result with findings, check results, and approval status.</returns>
    Task<ReviewResult> ReviewAsync(
        string projectId,
        IReadOnlyList<string> agentBranches,
        CancellationToken ct);

    /// <summary>
    /// Dispatches fix requests to agents based on review findings.
    /// Groups critical and major findings by task, creates FIX-REQUEST.md files,
    /// and tracks the fix round. After 2 failed rounds, escalates to the user.
    /// </summary>
    /// <param name="projectId">The internal project ID.</param>
    /// <param name="review">The review result containing findings to fix.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Fix round result with counts and remaining issues.</returns>
    Task<FixRoundResult> DispatchFixesAsync(
        string projectId,
        ReviewResult review,
        CancellationToken ct);

    /// <summary>
    /// Gets the current review status for a project.
    /// </summary>
    /// <param name="projectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current review status including round count and finding resolution.</returns>
    Task<ReviewStatus> GetReviewStatusAsync(
        string projectId,
        CancellationToken ct);
}
