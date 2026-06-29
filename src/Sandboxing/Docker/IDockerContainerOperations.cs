namespace Edda.Sandboxing.Docker;

/// <summary>
/// Abstracts the Docker container lifecycle operations needed by <see cref="DockerSandbox"/>.
/// Allows unit tests to mock Docker interactions without a running Docker daemon.
/// </summary>
public interface IDockerContainerOperations
{
    /// <summary>
    /// Creates a restricted Python container with no network access and memory/CPU limits,
    /// starts it, and returns the container ID.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Docker container ID of the started container.</returns>
    Task<string> CreateAndStartContainerAsync(CancellationToken ct);

    /// <summary>
    /// Copies a file into the container at the specified path using a tar archive.
    /// </summary>
    /// <param name="containerId">Target container ID.</param>
    /// <param name="remotePath">Absolute path inside the container (e.g. <c>/tmp/validator.py</c>).</param>
    /// <param name="content">UTF-8 file content to copy.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CopyFileAsync(string containerId, string remotePath, string content, CancellationToken ct);

    /// <summary>
    /// Executes a shell command inside the container and waits for it to finish.
    /// Returns stdout, stderr, the exit code, and whether execution timed out.
    /// </summary>
    /// <param name="containerId">Target container ID.</param>
    /// <param name="cmd">Command and arguments array.</param>
    /// <param name="timeoutSeconds">Maximum execution time in seconds before the operation is cancelled.</param>
    /// <param name="ct">Outer cancellation token linked with the timeout.</param>
    /// <returns>
    /// A tuple of (Stdout, Stderr, ExitCode, TimedOut).
    /// <c>TimedOut</c> is <see langword="true"/> when the timeout was reached.
    /// </returns>
    Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> ExecAsync(
        string containerId, string[] cmd, int timeoutSeconds, CancellationToken ct);

    /// <summary>
    /// Stops the container. If <c>AutoRemove</c> was set on creation, Docker removes it automatically.
    /// </summary>
    /// <param name="containerId">Target container ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopContainerAsync(string containerId, CancellationToken ct);
}
