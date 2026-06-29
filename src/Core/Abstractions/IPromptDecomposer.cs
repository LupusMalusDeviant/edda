using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Decomposes a Pflichtenheft into agent-specific prompt packages.
/// Each package contains everything one Claude Code instance needs:
/// CONTEXT.md (AKG rules, architecture), TASK.md (work instructions),
/// API-CONTRACT.md (cross-references), and PROTOTYPE.md (UI reference).
/// </summary>
public interface IPromptDecomposer
{
    /// <summary>
    /// Decomposes a Pflichtenheft into agent-specific prompt packages.
    /// Each package contains everything one Claude Code instance needs.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID from ASPS import.</param>
    /// <param name="plan">The development plan from Step 03.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Map of task ID to agent prompt package.</returns>
    Task<IReadOnlyDictionary<string, AgentPromptPackage>> DecomposeAsync(
        string internalProjectId,
        DevPlan plan,
        CancellationToken ct);

    /// <summary>
    /// Validates that all Pflichtenheft chapters are covered by agent prompts.
    /// </summary>
    /// <param name="prompts">The generated prompt packages.</param>
    /// <param name="pflichtenheftContent">The full Pflichtenheft markdown content.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Coverage report with covered/uncovered chapters and warnings.</returns>
    Task<PromptCoverageReport> ValidateCoverageAsync(
        IReadOnlyDictionary<string, AgentPromptPackage> prompts,
        string pflichtenheftContent,
        CancellationToken ct);
}
