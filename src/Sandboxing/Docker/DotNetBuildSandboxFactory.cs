using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Docker;

/// <summary>
/// Creates <see cref="DotNetBuildSandbox"/> instances backed by isolated Docker containers
/// running the .NET SDK image. Unlike <see cref="DockerSandboxFactory"/> (Python, no network),
/// build containers use bridge networking for NuGet restore and higher resource limits.
/// </summary>
public sealed class DotNetBuildSandboxFactory : IDotNetBuildSandboxFactory
{
    private readonly IDotNetBuildContainerOps _ops;
    private readonly ILogger<DotNetBuildSandbox> _sandboxLogger;

    /// <summary>
    /// Initializes a new <see cref="DotNetBuildSandboxFactory"/>.
    /// </summary>
    /// <param name="ops">Docker container operations abstraction for .NET SDK containers.</param>
    /// <param name="sandboxLogger">Logger forwarded to each created <see cref="DotNetBuildSandbox"/>.</param>
    internal DotNetBuildSandboxFactory(
        IDotNetBuildContainerOps ops,
        ILogger<DotNetBuildSandbox> sandboxLogger)
    {
        _ops = ops;
        _sandboxLogger = sandboxLogger;
    }

    /// <inheritdoc />
    public async Task<IDotNetBuildSandbox> CreateAsync(CancellationToken ct = default)
    {
        var containerId = await _ops.CreateAndStartAsync(ct).ConfigureAwait(false);

        _sandboxLogger.LogDebug(
            "DotNetBuildSandbox container ready: {ContainerId}", containerId[..12]);

        return new DotNetBuildSandbox(_ops, containerId, _sandboxLogger);
    }
}
