using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Persists queued tasks for deferred agent execution.
/// The TaskQueueWorker polls this store to find and execute due tasks.
/// </summary>
public interface ITaskQueueStore
{
    /// <summary>
    /// Adds a task to the queue.
    /// </summary>
    /// <param name="task">The task to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted task with assigned TaskId.</returns>
    Task<QueuedTask> EnqueueAsync(QueuedTask task, CancellationToken ct = default);

    /// <summary>
    /// Returns all tasks that are due for execution as of the given time.
    /// Only returns tasks with Status=Pending and ExecuteAfter &lt;= asOf.
    /// </summary>
    /// <param name="asOf">The reference time (use TimeProvider.GetUtcNow()).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Due tasks ordered by Priority descending, then ExecuteAfter ascending.</returns>
    Task<IReadOnlyList<QueuedTask>> GetDueTasksAsync(
        DateTimeOffset asOf,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the execution status of a task.
    /// </summary>
    /// <param name="taskId">The task to update.</param>
    /// <param name="status">The new status.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetStatusAsync(
        string taskId,
        TaskExecutionStatus status,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a task by ID, or null if not found.
    /// </summary>
    Task<QueuedTask?> GetByIdAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// Cancels a pending task. Only the owning user can cancel their tasks.
    /// </summary>
    /// <param name="taskId">The task to cancel.</param>
    /// <param name="userId">Must match the task's owner.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CancelAsync(string taskId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Persists the result (or error) of a completed or failed task.
    /// Updates Status, ResultContent or ErrorMessage, and CompletedAt atomically.
    /// </summary>
    /// <param name="taskId">The task to update.</param>
    /// <param name="status">Must be Completed or Failed.</param>
    /// <param name="resultContent">The agent response content (on success).</param>
    /// <param name="errorMessage">The error message (on failure).</param>
    /// <param name="completedAt">UTC timestamp of completion.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetResultAsync(
        string taskId,
        TaskExecutionStatus status,
        string? resultContent,
        string? errorMessage,
        DateTimeOffset completedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Returns completed or failed tasks for a user, ordered by completion time descending.
    /// Used for audit queries and result retrieval.
    /// </summary>
    /// <param name="userId">The owning user.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<QueuedTask>> GetCompletedTasksAsync(
        string userId,
        int limit = 20,
        CancellationToken ct = default);
}
