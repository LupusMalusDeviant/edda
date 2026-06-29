using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Multi-agent prototype build orchestrator. Decides per build whether to
/// run all sub-agents in parallel (initial build / structural feedback) or
/// only the affected ones (incremental feedback), merges their contributions
/// deterministically via <see cref="IConsistencyKeeper"/>, and copies the
/// prototype runtime assets into the version directory.
/// </summary>
/// <remarks>
/// The orchestrator is the Phase-4 replacement for the monolithic
/// <see cref="IPrototypeBuilder"/>. Selection between the two paths happens
/// in <c>AspsStepExecutor</c> based on the <c>USE_MULTI_AGENT_PROTOTYPE</c>
/// environment flag — callers should prefer this interface once the flag
/// is stable in their environment.
/// </remarks>
public interface IPrototypeOrchestrator
{
    /// <summary>
    /// Runs a multi-agent build for the given project. Loads the previous
    /// manifest (if any), classifies the feedback scope, executes the
    /// appropriate sub-agents, merges contributions, and persists a new
    /// version under <c>data/prototypes/{projectId}/v{N}/</c>.
    /// </summary>
    /// <param name="internalProjectId">Internal project identifier from the ASPS mapping store.</param>
    /// <param name="feedback">Optional natural-language feedback from ASPS.ai. Null for initial build.</param>
    /// <param name="userId">User who triggered the build.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Build result compatible with <see cref="PrototypeBuildResult"/>.</returns>
    Task<PrototypeBuildResult> BuildAsync(
        string internalProjectId,
        string? feedback,
        string userId,
        CancellationToken ct);
}
