using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Communication channel between the main agent and a clone container.
/// Two implementations are available: HttpCloneChannel (default) and RedisStreamCloneChannel.
/// Selection is controlled by the CLONE_CHANNEL=http|redis environment variable.
/// </summary>
public interface ICloneChannel
{
    /// <summary>
    /// Sends a task to a running clone container.
    /// </summary>
    /// <param name="cloneEndpoint">The base URL of the clone's HTTP API.</param>
    /// <param name="task">The task to dispatch to the clone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assigned task ID for subsequent status and result queries.</returns>
    Task<string> SendTaskAsync(
        string cloneEndpoint,
        CloneTask task,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries the execution status of a running clone task.
    /// </summary>
    /// <param name="cloneEndpoint">The base URL of the clone's HTTP API.</param>
    /// <param name="taskId">The task ID returned by SendTaskAsync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current status of the clone task.</returns>
    Task<CloneStatus> GetStatusAsync(
        string cloneEndpoint,
        string taskId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the result of a completed clone task.
    /// </summary>
    /// <param name="cloneEndpoint">The base URL of the clone's HTTP API.</param>
    /// <param name="taskId">The task ID returned by SendTaskAsync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final result produced by the clone.</returns>
    Task<CloneResult> GetResultAsync(
        string cloneEndpoint,
        string taskId,
        CancellationToken cancellationToken = default);
}
