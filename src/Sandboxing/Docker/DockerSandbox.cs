using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Docker;

/// <summary>
/// A single-use Docker container sandbox for executing Python scripts in isolation.
/// Created by <see cref="DockerSandboxFactory"/>. Disposes the container on <see cref="DisposeAsync"/>.
/// </summary>
public sealed class DockerSandbox : ISandbox
{
    private const int DefaultTimeoutSeconds = 10;
    private const string ScriptPath = "/workspace/script.py";
    private const string InputJsonPath = "/workspace/input.json";

    private readonly IDockerContainerOperations _ops;
    private readonly string _containerId;
    private readonly ILogger<DockerSandbox> _logger;

    /// <summary>
    /// Initializes a new <see cref="DockerSandbox"/> with an already-running container.
    /// </summary>
    /// <param name="ops">Docker container operations abstraction.</param>
    /// <param name="containerId">ID of the running Docker container.</param>
    /// <param name="logger">Structured logger.</param>
    public DockerSandbox(IDockerContainerOperations ops, string containerId, ILogger<DockerSandbox> logger)
    {
        _ops = ops;
        _containerId = containerId;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SandboxResult> ExecuteAsync(
        string scriptContent,
        string jsonInput,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DockerSandbox executing script in container {ContainerId}", _containerId[..12]);

        // Copy script and input into the container workspace
        await _ops.CopyFileAsync(_containerId, ScriptPath, scriptContent, cancellationToken);
        await _ops.CopyFileAsync(_containerId, InputJsonPath, jsonInput, cancellationToken);

        // Execute: cd /workspace && python3 script.py < input.json
        var (stdout, stderr, exitCode, timedOut) = await _ops.ExecAsync(
            _containerId,
            ["/bin/sh", "-c", $"cd /workspace && python3 {ScriptPath} < {InputJsonPath}"],
            DefaultTimeoutSeconds,
            cancellationToken);

        if (timedOut)
            _logger.LogWarning("DockerSandbox script timed out after {Seconds}s", DefaultTimeoutSeconds);
        else
            _logger.LogDebug("DockerSandbox script completed exitCode={ExitCode}", exitCode);

        return new SandboxResult
        {
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            TimedOut = timedOut
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _ops.StopContainerAsync(_containerId, CancellationToken.None);
    }
}
