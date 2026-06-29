using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Monitors ASPS agent execution, tracks progress, handles retries, and coordinates
/// layer transitions. Wraps <see cref="IAspsAgentSpawner"/> with monitoring,
/// notifications, and error handling.
/// </summary>
public interface IAspsMonitor
{
    /// <summary>
    /// Spawns all agents and monitors their execution until completion or failure.
    /// Handles layer transitions, retries failed agents once, and sends notifications
    /// via delivery channels at significant events (layer start, completion, errors).
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="plan">The development plan with tasks and topology.</param>
    /// <param name="prompts">Map of task ID to agent prompt package.</param>
    /// <param name="userId">The user who owns the project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Final project progress snapshot with all agent results.</returns>
    Task<ProjectProgressSnapshot> ExecuteAndMonitorAsync(
        string internalProjectId,
        DevPlan plan,
        IReadOnlyDictionary<string, AgentPromptPackage> prompts,
        string userId,
        CancellationToken ct);

    /// <summary>
    /// Gets the current progress snapshot for a running or completed project.
    /// </summary>
    /// <param name="internalProjectId">The internal project ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current project progress snapshot.</returns>
    Task<ProjectProgressSnapshot> GetProgressAsync(
        string internalProjectId,
        CancellationToken ct);
}
