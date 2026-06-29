using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists Hand state, definitions and progress entries in a durable store.
/// Implemented by <c>HandProgressStore</c> using SQLite.
/// </summary>
public interface IHandProgressStore
{
    /// <summary>
    /// Returns the number of Hands for the given user that are in an active state
    /// (i.e. <see cref="HandState.Active"/> or <see cref="HandState.Running"/>).
    /// </summary>
    /// <param name="userId">The user whose active Hands should be counted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Count of active Hands.</returns>
    Task<int> CountActiveAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Persists a new Hand using both the handle snapshot and its full definition.
    /// </summary>
    /// <param name="handle">The initial hand reference including ID, name and state.</param>
    /// <param name="definition">The full creation parameters for the Hand.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(HandHandle handle, HandDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Updates the lifecycle state of an existing Hand.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="state">New state to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetStateAsync(string handId, HandState state, CancellationToken ct = default);

    /// <summary>
    /// Updates the last-run timestamp of an existing Hand.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="lastRunAt">Timestamp of the most recent cycle completion.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetLastRunAtAsync(string handId, DateTimeOffset lastRunAt, CancellationToken ct = default);

    /// <summary>
    /// Updates the goal/directive of an existing Hand.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="newGoal">The updated goal description.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateGoalAsync(string handId, string newGoal, CancellationToken ct = default);

    /// <summary>
    /// Appends a new progress entry written after a completed or failed cycle.
    /// </summary>
    /// <param name="entry">The progress entry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AppendProgressAsync(HandProgressEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent progress entries for a Hand, ordered newest first.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="limit">Maximum number of entries to return. Default: 20.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Most recent progress entries, newest first.</returns>
    Task<IReadOnlyList<HandProgressEntry>> GetProgressAsync(
        string handId, int limit = 20, CancellationToken ct = default);

    /// <summary>
    /// Returns the aggregated status of a Hand including cycle statistics.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated hand status, or <c>null</c> if the Hand does not exist.</returns>
    Task<HandStatus?> GetStatusAsync(string handId, CancellationToken ct = default);

    /// <summary>
    /// Returns the full definition of a Hand as stored at creation time, or after the last directive update.
    /// </summary>
    /// <param name="handId">Unique Hand identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The hand definition, or <c>null</c> if not found.</returns>
    Task<HandDefinition?> GetDefinitionAsync(string handId, CancellationToken ct = default);

    /// <summary>
    /// Returns all Hands belonging to the specified user.
    /// </summary>
    /// <param name="userId">The user whose Hands should be returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of hand handles for the user.</returns>
    Task<IReadOnlyList<HandHandle>> ListAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns all Hands whose state is <see cref="HandState.Active"/> or <see cref="HandState.Running"/>,
    /// together with their definitions. Used at startup to resume persistent Hands.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pairs of (handle, definition) for all resumable Hands.</returns>
    Task<IReadOnlyList<(HandHandle Handle, HandDefinition Definition)>> GetAllActiveHandsAsync(
        CancellationToken ct = default);
}
