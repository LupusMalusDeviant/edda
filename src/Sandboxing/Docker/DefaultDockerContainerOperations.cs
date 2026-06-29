using System.Formats.Tar;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Docker;

/// <summary>
/// Production implementation of <see cref="IDockerContainerOperations"/> using the Docker.DotNet client.
/// Creates Python containers with configurable network access and strict resource limits.
/// The Python Docker image can be configured via the <c>SANDBOX_PYTHON_IMAGE</c> environment variable
/// (default: <c>python:3.12-slim</c>). Network mode defaults to <c>"none"</c> (fully isolated)
/// but can be set to <c>"bridge"</c> for containers that need HTTP access.
/// </summary>
public sealed class DefaultDockerContainerOperations : IDockerContainerOperations
{
    /// <summary>Default Python Docker image used when <c>SANDBOX_PYTHON_IMAGE</c> is not set.</summary>
    internal const string DefaultPythonImage = "python:3.12-slim";

    /// <summary>Network mode for fully isolated containers (no network access).</summary>
    internal const string NetworkNone = "none";

    /// <summary>Network mode for containers that need outbound HTTP access.</summary>
    internal const string NetworkBridge = "bridge";

    private const long MemoryLimitBytes = 256 * 1024 * 1024; // 256 MB
    private const long CpuPeriod = 100_000;
    private const long CpuQuota = 50_000; // 50% CPU

    private readonly DockerClient _dockerClient;
    private readonly string _pythonImage;
    private readonly string _networkMode;
    private readonly ILogger<DefaultDockerContainerOperations> _logger;

    /// <summary>
    /// Initializes a new <see cref="DefaultDockerContainerOperations"/> using the default Docker socket.
    /// </summary>
    /// <param name="logger">Structured logger.</param>
    /// <param name="pythonImage">
    /// Docker image name for Python sandboxes. When <see langword="null"/>,
    /// falls back to <see cref="DefaultPythonImage"/>.
    /// </param>
    /// <param name="networkMode">
    /// Docker network mode for created containers. Use <c>"none"</c> for full isolation
    /// (TDK validators) or <c>"bridge"</c> for outbound HTTP access (custom tools, code interpreter).
    /// Defaults to <c>"none"</c>.
    /// </param>
    public DefaultDockerContainerOperations(
        ILogger<DefaultDockerContainerOperations> logger,
        string? pythonImage = null,
        string networkMode = NetworkNone)
    {
        _dockerClient = new DockerClientConfiguration().CreateClient();
        _pythonImage = pythonImage ?? DefaultPythonImage;
        _networkMode = networkMode;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CreateAndStartContainerAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "Creating sandbox container with image={Image} networkMode={NetworkMode}",
            _pythonImage, _networkMode);

        var response = await _dockerClient.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = _pythonImage,
                Cmd = ["/bin/sh", "-c", "mkdir -p /workspace && while true; do sleep 1; done"],
                WorkingDir = "/workspace",
                HostConfig = new HostConfig
                {
                    NetworkMode = _networkMode,
                    Memory = MemoryLimitBytes,
                    CPUPeriod = CpuPeriod,
                    CPUQuota = CpuQuota,
                    AutoRemove = true
                }
            }, ct);

        await _dockerClient.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);
        _logger.LogDebug("DockerSandbox container started: {ContainerId}", response.ID[..12]);
        return response.ID;
    }

    /// <inheritdoc />
    public async Task CopyFileAsync(string containerId, string remotePath, string content, CancellationToken ct)
    {
        var fileName = System.IO.Path.GetFileName(remotePath);
        var directory = System.IO.Path.GetDirectoryName(remotePath) ?? "/tmp";

        await using var tarStream = CreateTarStream(fileName, content);
        await _dockerClient.Containers.ExtractArchiveToContainerAsync(
            containerId,
            new ContainerPathStatParameters { Path = directory },
            tarStream,
            ct);
    }

    /// <inheritdoc />
    public async Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> ExecAsync(
        string containerId, string[] cmd, int timeoutSeconds, CancellationToken ct)
    {
        var execResponse = await _dockerClient.Exec.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = cmd,
                AttachStdout = true,
                AttachStderr = true
            }, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        string stdout = string.Empty;
        string stderr = string.Empty;
        int exitCode = 1;
        bool timedOut = false;

        try
        {
            var stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(
                execResponse.ID, false, cts.Token);
            (stdout, stderr) = await stream.ReadOutputToEndAsync(cts.Token);

            var inspect = await _dockerClient.Exec.InspectContainerExecAsync(execResponse.ID, ct);
            exitCode = (int)inspect.ExitCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            _logger.LogWarning("DockerSandbox exec timed out after {Seconds}s", timeoutSeconds);
        }

        return (stdout, stderr, exitCode, timedOut);
    }

    /// <inheritdoc />
    public async Task StopContainerAsync(string containerId, CancellationToken ct)
    {
        try
        {
            await _dockerClient.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 0 },
                ct);
            _logger.LogDebug("DockerSandbox container stopped: {ContainerId}", containerId[..12]);
        }
        catch (Exception ex)
        {
            // Container may have already been removed (e.g. by AutoRemove)
            _logger.LogDebug(ex, "DockerSandbox stop ignored (container may already be gone): {ContainerId}", containerId[..12]);
        }
    }

    private static MemoryStream CreateTarStream(string fileName, string content)
    {
        var ms = new MemoryStream();
        using var writer = new TarWriter(ms, TarEntryFormat.Pax, leaveOpen: true);
        var bytes = Encoding.UTF8.GetBytes(content);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
        {
            DataStream = new MemoryStream(bytes)
        };
        writer.WriteEntry(entry);
        ms.Position = 0;
        return ms;
    }
}
