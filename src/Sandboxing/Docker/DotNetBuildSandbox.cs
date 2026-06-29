using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Docker;

/// <summary>
/// Build sandbox backed by a Docker container running the .NET SDK.
/// Supports copying source files, running <c>dotnet build</c>, and extracting build artifacts.
/// Uses <see cref="IDotNetBuildContainerOps"/> for all Docker interactions,
/// allowing unit tests to mock the Docker layer.
/// </summary>
public sealed class DotNetBuildSandbox : IDotNetBuildSandbox
{
    private const int BuildTimeoutSeconds = 120;
    private const string SourceRoot = "/src";

    private readonly IDotNetBuildContainerOps _ops;
    private readonly string _containerId;
    private readonly ILogger<DotNetBuildSandbox> _logger;

    /// <summary>
    /// Initializes a new <see cref="DotNetBuildSandbox"/> with an already-running container.
    /// </summary>
    /// <param name="ops">Docker container operations abstraction for .NET builds.</param>
    /// <param name="containerId">ID of the running Docker container.</param>
    /// <param name="logger">Structured logger.</param>
    internal DotNetBuildSandbox(
        IDotNetBuildContainerOps ops,
        string containerId,
        ILogger<DotNetBuildSandbox> logger)
    {
        _ops = ops;
        _containerId = containerId;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CopySourceAsync(
        IReadOnlyDictionary<string, string> files,
        CancellationToken ct = default)
    {
        foreach (var (relativePath, content) in files)
        {
            var remotePath = $"{SourceRoot}/{relativePath}";

            // Ensure parent directory exists
            var dir = GetParentPath(remotePath);
            await _ops.ExecAsync(
                _containerId,
                ["/bin/sh", "-c", $"mkdir -p {dir}"],
                timeoutSeconds: 10, ct).ConfigureAwait(false);

            await _ops.CopyFileAsync(
                _containerId, remotePath, content, ct).ConfigureAwait(false);
        }

        _logger.LogDebug("Copied {Count} source files to build sandbox", files.Count);
    }

    /// <inheritdoc />
    public async Task<BuildSandboxResult> BuildAsync(
        string projectName,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Building project {Project} in sandbox", projectName);

        var (stdout, stderr, exitCode, timedOut) = await _ops.ExecAsync(
            _containerId,
            ["/bin/sh", "-c", $"cd {SourceRoot}/{projectName} && dotnet build -c Release --nologo"],
            BuildTimeoutSeconds, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Build completed: exitCode={ExitCode}, timedOut={TimedOut}", exitCode, timedOut);

        return new BuildSandboxResult
        {
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            TimedOut = timedOut
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, byte[]>> ExtractArtifactsAsync(
        string projectName,
        CancellationToken ct = default)
    {
        var outputDir = $"{SourceRoot}/{projectName}/bin/Release/net10.0";

        // List files in output directory
        var (listing, _, exitCode, _) = await _ops.ExecAsync(
            _containerId,
            ["/bin/sh", "-c", $"ls -1 {outputDir} 2>/dev/null"],
            timeoutSeconds: 10, ct).ConfigureAwait(false);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(listing))
        {
            _logger.LogWarning("No build artifacts found at {Path}", outputDir);
            return new Dictionary<string, byte[]>();
        }

        var artifacts = new Dictionary<string, byte[]>();
        var fileNames = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var fileName in fileNames)
        {
            // Only extract DLLs and deps.json files
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var remotePath = $"{outputDir}/{fileName}";

            // Read file content as base64
            var (base64Content, _, readExitCode, _) = await _ops.ExecAsync(
                _containerId,
                ["/bin/sh", "-c", $"base64 -w0 {remotePath}"],
                timeoutSeconds: 30, ct).ConfigureAwait(false);

            if (readExitCode == 0 && !string.IsNullOrWhiteSpace(base64Content))
            {
                artifacts[fileName] = Convert.FromBase64String(base64Content.Trim());
            }
        }

        _logger.LogInformation(
            "Extracted {Count} build artifacts from sandbox", artifacts.Count);

        return artifacts;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _ops.StopAsync(_containerId, CancellationToken.None).ConfigureAwait(false);

            _logger.LogDebug(
                "DotNetBuildSandbox container stopped: {ContainerId}", _containerId[..12]);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "DotNetBuildSandbox stop ignored (container may already be gone): {ContainerId}",
                _containerId[..12]);
        }
    }

    private static string GetParentPath(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash > 0 ? path[..lastSlash] : "/";
    }
}
