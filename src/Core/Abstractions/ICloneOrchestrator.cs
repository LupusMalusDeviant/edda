using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Manages the full lifecycle of clone agent containers.
/// Implemented by ContainerOrchestrator using Docker.DotNet.
/// </summary>
public interface ICloneOrchestrator
{
    /// <summary>
    /// Spawns a new clone container and sends it an initial task.
    /// Enforces the maximum clone limit (5 concurrent clones).
    /// </summary>
    /// <param name="cloneId">Unique identifier for this clone instance.</param>
    /// <param name="userId">User context to propagate into the clone.</param>
    /// <param name="task">The task to send to the clone after startup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A handle containing the clone ID and its HTTP endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the maximum clone limit is reached.</exception>
    Task<CloneHandle> SpawnAsync(
        string cloneId,
        string userId,
        CloneTask task,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current status of a running clone.
    /// </summary>
    /// <param name="cloneId">The clone identifier returned by SpawnAsync.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current lifecycle state of the clone.</returns>
    Task<CloneStatus> GetStatusAsync(
        string cloneId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the text result produced by a completed clone.
    /// </summary>
    /// <param name="cloneId">The clone identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The clone's output, or null if not yet available.</returns>
    Task<string?> GetResultAsync(
        string cloneId,
        CancellationToken ct = default);

    /// <summary>
    /// Stops and removes a clone container.
    /// </summary>
    /// <param name="cloneId">The clone identifier to terminate.</param>
    /// <param name="ct">Cancellation token.</param>
    Task TerminateAsync(string cloneId, CancellationToken ct = default);

    /// <summary>
    /// Removes stale clone containers older than 2 hours with the agent.managed=true label.
    /// Called periodically by the CloneLifecycleManager.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task CleanupStaleAsync(CancellationToken ct = default);
}
