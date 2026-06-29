using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Spawns and monitors Claude Code agent containers for ASPS.ai projects.
/// Orchestrates worktree creation, prompt package deployment, container spawning,
/// and dependency-aware parallel execution based on the DevPlan topology.
/// </summary>
public interface IAspsAgentSpawner
{
    /// <summary>
    /// Spawns all agents for an ASPS project based on the DevPlan and prompt packages.
    /// Respects the dependency DAG: agents with no dependencies start first,
    /// dependent agents wait until predecessors complete.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="plan">The development plan with tasks and topology.</param>
    /// <param name="prompts">Map of task ID to agent prompt package.</param>
    /// <param name="userId">The user who owns the project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Spawn result with per-agent status and repo info.</returns>
    Task<AspsSpawnResult> SpawnAllAsync(
        string internalProjectId,
        DevPlan plan,
        IReadOnlyDictionary<string, AgentPromptPackage> prompts,
        string userId,
        CancellationToken ct);

    /// <summary>
    /// Gets the current status of all spawned agents for a project.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Per-agent status including commit counts and errors.</returns>
    Task<IReadOnlyDictionary<string, DevAgentStatus>> GetAgentStatusAsync(
        string internalProjectId,
        CancellationToken ct);

    /// <summary>
    /// Cleans up all worktrees and temporary branches for a project.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CleanupAsync(
        string internalProjectId,
        CancellationToken ct);
}
