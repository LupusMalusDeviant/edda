using System.Formats.Tar;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Docker;

/// <summary>
/// Production implementation of <see cref="IDotNetBuildContainerOps"/> using the Docker.DotNet client.
/// Creates .NET SDK build containers with bridge networking (NuGet restore requires network access)
/// and higher resource limits than the Python sandbox.
/// </summary>
internal sealed class DefaultDotNetBuildContainerOps : IDotNetBuildContainerOps
{
    /// <summary>Default .NET SDK Docker image used when <c>DOTNET_SDK_IMAGE</c> is not set.</summary>
    internal const string DefaultSdkImage = "mcr.microsoft.com/dotnet/sdk:10.0";

    private const long MemoryLimitBytes = 512 * 1024 * 1024; // 512 MB
    private const long CpuPeriod = 100_000;
    private const long CpuQuota = 50_000; // 50% CPU

    private readonly DockerClient _dockerClient;
    private readonly string _sdkImage;
    private readonly ILogger<DefaultDotNetBuildContainerOps> _logger;

    /// <summary>
    /// Initializes a new <see cref="DefaultDotNetBuildContainerOps"/> using the default Docker socket.
    /// </summary>
    /// <param name="logger">Structured logger.</param>
    /// <param name="sdkImage">
    /// Docker image name for .NET SDK build containers. When <see langword="null"/>,
    /// falls back to <see cref="DefaultSdkImage"/>.
    /// </param>
    public DefaultDotNetBuildContainerOps(
        ILogger<DefaultDotNetBuildContainerOps> logger,
        string? sdkImage = null)
    {
        _dockerClient = new DockerClientConfiguration().CreateClient();
        _sdkImage = sdkImage ?? DefaultSdkImage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CreateAndStartAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "Creating .NET build container with image {Image}", _sdkImage);

        var response = await _dockerClient.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = _sdkImage,
                Cmd = ["/bin/sh", "-c", "mkdir -p /src && while true; do sleep 1; done"],
                WorkingDir = "/src",
                HostConfig = new HostConfig
                {
                    NetworkMode = "bridge", // NuGet restore needs network access
                    Memory = MemoryLimitBytes,
                    CPUPeriod = CpuPeriod,
                    CPUQuota = CpuQuota,
                    AutoRemove = true
                }
            }, ct).ConfigureAwait(false);

        await _dockerClient.Containers.StartContainerAsync(
            response.ID, new ContainerStartParameters(), ct).ConfigureAwait(false);

        _logger.LogDebug(
            "DotNetBuild container started: {ContainerId}", response.ID[..12]);

        return response.ID;
    }

    /// <inheritdoc />
    public async Task CopyFileAsync(
        string containerId, string remotePath, string content, CancellationToken ct)
    {
        var fileName = GetFileName(remotePath);
        var directory = GetParentPath(remotePath);

        await using var tarStream = CreateTarStream(fileName, content);
        await _dockerClient.Containers.ExtractArchiveToContainerAsync(
            containerId,
            new ContainerPathStatParameters { Path = directory },
            tarStream,
            ct).ConfigureAwait(false);
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
            }, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        string stdout = string.Empty;
        string stderr = string.Empty;
        int exitCode = 1;
        bool timedOut = false;

        try
        {
            var stream = await _dockerClient.Exec.StartAndAttachContainerExecAsync(
                execResponse.ID, false, cts.Token).ConfigureAwait(false);
            (stdout, stderr) = await stream.ReadOutputToEndAsync(cts.Token).ConfigureAwait(false);

            var inspect = await _dockerClient.Exec.InspectContainerExecAsync(
                execResponse.ID, ct).ConfigureAwait(false);
            exitCode = (int)inspect.ExitCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            _logger.LogWarning(
                "DotNetBuild exec timed out after {Seconds}s", timeoutSeconds);
        }

        return (stdout, stderr, exitCode, timedOut);
    }

    /// <inheritdoc />
    public async Task StopAsync(string containerId, CancellationToken ct)
    {
        try
        {
            await _dockerClient.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 0 },
                ct).ConfigureAwait(false);

            _logger.LogDebug(
                "DotNetBuild container stopped: {ContainerId}", containerId[..12]);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "DotNetBuild stop ignored (container may already be gone): {ContainerId}",
                containerId[..12]);
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

    private static string GetFileName(string path) =>
        path[(path.LastIndexOf('/') + 1)..];

    private static string GetParentPath(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash > 0 ? path[..lastSlash] : "/";
    }
}
