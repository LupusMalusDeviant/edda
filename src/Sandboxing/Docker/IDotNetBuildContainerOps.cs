namespace Edda.Sandboxing.Docker;

/// <summary>
/// Abstracts Docker container operations needed by <see cref="DotNetBuildSandbox"/>.
/// Similar to <see cref="IDockerContainerOperations"/> but for .NET SDK build containers
/// with network access and higher resource limits.
/// </summary>
internal interface IDotNetBuildContainerOps
{
    /// <summary>
    /// Creates a .NET SDK container with bridge networking, starts it, and returns the container ID.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Docker container ID.</returns>
    Task<string> CreateAndStartAsync(CancellationToken ct);

    /// <summary>
    /// Copies a file into the container at the specified path using a tar archive.
    /// </summary>
    /// <param name="containerId">Target container ID.</param>
    /// <param name="remotePath">Absolute path inside the container.</param>
    /// <param name="content">UTF-8 file content to copy.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CopyFileAsync(string containerId, string remotePath, string content, CancellationToken ct);

    /// <summary>
    /// Executes a shell command inside the container.
    /// </summary>
    /// <param name="containerId">Target container ID.</param>
    /// <param name="cmd">Command and arguments array.</param>
    /// <param name="timeoutSeconds">Maximum execution time in seconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Stdout, Stderr, exit code, and whether the command timed out.</returns>
    Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> ExecAsync(
        string containerId, string[] cmd, int timeoutSeconds, CancellationToken ct);

    /// <summary>
    /// Stops and removes the container.
    /// </summary>
    /// <param name="containerId">Target container ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StopAsync(string containerId, CancellationToken ct);
}
