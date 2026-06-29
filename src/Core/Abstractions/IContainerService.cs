namespace Edda.Core.Abstractions;

/// <summary>
/// Provides read and control access to Docker containers on the host.
/// Used by the Container dashboard UI and REST API endpoints.
/// </summary>
public interface IContainerService
{
    /// <summary>
    /// Lists all Docker containers (running and stopped).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Snapshot of all containers with status metadata.</returns>
    Task<IReadOnlyList<Models.ContainerInfo>> ListContainersAsync(CancellationToken ct);

    /// <summary>
    /// Retrieves the last <paramref name="tail"/> log lines of a container.
    /// </summary>
    /// <param name="containerId">Container name or ID.</param>
    /// <param name="tail">Number of lines to return (default 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw log output as a string.</returns>
    Task<string> GetLogsAsync(string containerId, int tail = 100, CancellationToken ct = default);

    /// <summary>
    /// Retrieves real-time resource usage statistics for a container.
    /// </summary>
    /// <param name="containerId">Container name or ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>CPU, memory and network stats snapshot.</returns>
    Task<Models.ContainerStats> GetStatsAsync(string containerId, CancellationToken ct);

    /// <summary>
    /// Starts a stopped container.
    /// </summary>
    /// <param name="containerId">Container name or ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to control the agent's own container.</exception>
    Task StartAsync(string containerId, CancellationToken ct);

    /// <summary>
    /// Stops a running container.
    /// </summary>
    /// <param name="containerId">Container name or ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to control the agent's own container.</exception>
    Task StopAsync(string containerId, CancellationToken ct);

    /// <summary>
    /// Restarts a container.
    /// </summary>
    /// <param name="containerId">Container name or ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when attempting to control the agent's own container.</exception>
    Task RestartAsync(string containerId, CancellationToken ct);
}
