using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Docker;

/// <summary>
/// Creates <see cref="DockerSandbox"/> instances backed by isolated Docker containers.
/// Requires Docker to be running on the host. Uses a Python 3.12-slim image with no
/// network access, 256 MB RAM, and 50% CPU limits.
/// </summary>
public sealed class DockerSandboxFactory : ISandboxFactory
{
    private readonly IDockerContainerOperations _ops;
    private readonly ILogger<DockerSandbox> _sandboxLogger;

    /// <inheritdoc />
    public string SandboxType => "docker";

    /// <summary>
    /// Initializes a new <see cref="DockerSandboxFactory"/>.
    /// </summary>
    /// <param name="ops">Docker container lifecycle operations.</param>
    /// <param name="sandboxLogger">Logger forwarded to each created <see cref="DockerSandbox"/>.</param>
    public DockerSandboxFactory(IDockerContainerOperations ops, ILogger<DockerSandbox> sandboxLogger)
    {
        _ops = ops;
        _sandboxLogger = sandboxLogger;
    }

    /// <inheritdoc />
    public async Task<ISandbox> CreateAsync(CancellationToken cancellationToken = default)
    {
        var containerId = await _ops.CreateAndStartContainerAsync(cancellationToken);
        return new DockerSandbox(_ops, containerId, _sandboxLogger);
    }
}
