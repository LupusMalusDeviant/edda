using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Manages long-running autonomous worker instances (Hands).
/// Each Hand pursues a persistent goal, reports progress periodically,
/// and can spawn short-lived Clones for sub-tasks.
/// </summary>
public interface IHandsOrchestrator
{
    /// <summary>
    /// Creates and starts a new Hand with the given configuration.
    /// The Hand persists across agent restarts.
    /// </summary>
    /// <param name="definition">Configuration including name, goal, interval and report channel.</param>
    /// <param name="userId">User that owns this Hand.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A handle referencing the newly created Hand.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the user has already reached the maximum number of active Hands.
    /// </exception>
    Task<HandHandle> CreateHandAsync(
        HandDefinition definition,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the current status and cycle statistics of a Hand.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated hand status.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the Hand does not exist.</exception>
    Task<HandStatus> GetStatusAsync(
        string handId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the accumulated progress log entries for a Hand, newest first.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="limit">Maximum number of entries to return. Default: 20.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Progress entries, newest first.</returns>
    Task<IReadOnlyList<HandProgressEntry>> GetProgressAsync(
        string handId,
        int limit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a directive update to a running Hand, changing the goal it pursues.
    /// The updated goal is persisted and used in the next cycle.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="newDirective">The new goal description for the Hand.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when the Hand does not exist.</exception>
    Task UpdateDirectiveAsync(
        string handId,
        string newDirective,
        CancellationToken ct = default);

    /// <summary>
    /// Stops a running Hand. The worker is cancelled and the state is set to
    /// <see cref="HandState.Stopped"/>. Progress history is retained.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopAsync(string handId, CancellationToken ct = default);

    /// <summary>
    /// Returns all Hands owned by the given user.
    /// </summary>
    /// <param name="userId">The user whose Hands should be listed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of hand handles for the user.</returns>
    Task<IReadOnlyList<HandHandle>> ListAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes all persisted Hands whose state is <see cref="HandState.Active"/> or
    /// <see cref="HandState.Running"/> by restarting their worker loops.
    /// Called once by <c>HandsStartupService</c> when the host starts.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ResumePersistedHandsAsync(CancellationToken ct = default);
}
