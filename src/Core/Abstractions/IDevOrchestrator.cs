using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates multi-agent software development projects.
/// Analyzes specifications, decomposes into tasks, spawns coding agents,
/// monitors progress, merges results, and produces working prototypes.
/// </summary>
public interface IDevOrchestrator
{
    /// <summary>
    /// Analyzes a specification and creates a development plan with task decomposition,
    /// agent assignments, dependency graph, and git branch strategy.
    /// </summary>
    /// <param name="spec">The specification or feature description to analyze.</param>
    /// <param name="config">Project configuration (repo URL, CLI type, concurrency, etc.).</param>
    /// <param name="userId">Owner of the development project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A development plan ready for user review and execution.</returns>
    Task<DevPlan> AnalyzeAndPlanAsync(
        string spec,
        DevProjectConfig config,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a development plan: spawns coding agents, monitors progress,
    /// merges results. Returns immediately; use <see cref="GetStatusAsync"/> to monitor.
    /// </summary>
    /// <param name="plan">The approved development plan to execute.</param>
    /// <param name="userId">Owner of the project.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A handle to the running project.</returns>
    Task<DevProjectHandle> ExecuteAsync(
        DevPlan plan,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current status of a running or completed dev project.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detailed project status including per-agent states.</returns>
    Task<DevProjectStatus> GetStatusAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the execution log for a dev project.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="limit">Maximum number of log entries to return. Default: 50.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Log entries ordered by timestamp descending.</returns>
    Task<IReadOnlyList<DevProjectLogEntry>> GetLogAsync(
        string projectId,
        int limit = 50,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels a running dev project. Already-completed agent work is preserved.
    /// </summary>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CancelAsync(
        string projectId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all dev projects for the given user.
    /// </summary>
    /// <param name="userId">Owner to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Project handles ordered by creation date descending.</returns>
    Task<IReadOnlyList<DevProjectHandle>> ListAsync(
        string userId,
        CancellationToken ct = default);
}
