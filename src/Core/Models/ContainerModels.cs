namespace Edda.Core.Models;

/// <summary>
/// Snapshot of a Docker container's metadata and status.
/// </summary>
/// <param name="Id">Short container ID (12 chars).</param>
/// <param name="Name">Primary container name (without leading slash).</param>
/// <param name="Image">Docker image name with tag.</param>
/// <param name="Status">Human-readable status string (e.g. "Up 2 hours (healthy)").</param>
/// <param name="State">Container state: running, exited, created, paused, restarting, dead.</param>
/// <param name="Created">Container creation timestamp.</param>
/// <param name="Ports">Formatted port mappings (e.g. "0.0.0.0:999->5000/tcp").</param>
/// <param name="Labels">Container labels for filtering (e.g. agent.managed=true).</param>
/// <param name="IsManaged">True if the container was created by the agent (label agent.managed=true).</param>
/// <param name="IsSelf">True if this is the agent's own container.</param>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string Status,
    string State,
    DateTimeOffset Created,
    IReadOnlyList<string> Ports,
    IReadOnlyDictionary<string, string> Labels,
    bool IsManaged,
    bool IsSelf);

/// <summary>
/// Real-time resource usage statistics for a Docker container.
/// </summary>
/// <param name="CpuPercent">CPU usage as a percentage (0–100 × number of CPUs).</param>
/// <param name="MemoryUsage">Current memory usage in bytes.</param>
/// <param name="MemoryLimit">Memory limit in bytes (0 if unlimited).</param>
/// <param name="NetworkRx">Total bytes received across all network interfaces.</param>
/// <param name="NetworkTx">Total bytes transmitted across all network interfaces.</param>
public sealed record ContainerStats(
    double CpuPercent,
    long MemoryUsage,
    long MemoryLimit,
    long NetworkRx,
    long NetworkTx);
