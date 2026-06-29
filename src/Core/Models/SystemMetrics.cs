namespace Edda.Core.Models;

/// <summary>
/// Point-in-time system resource metrics for use in SystemMetricTrigger evaluation.
/// </summary>
/// <param name="CpuPercent">Current CPU utilization as a percentage (0–100).</param>
/// <param name="MemoryUsedBytes">Currently used physical memory in bytes.</param>
/// <param name="MemoryTotalBytes">Total available physical memory in bytes.</param>
/// <param name="Disks">Per-mount-point disk usage statistics.</param>
public sealed record SystemMetrics(
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    IReadOnlyDictionary<string, DiskMetrics> Disks);

/// <summary>
/// Disk usage statistics for a single mount point or drive.
/// </summary>
/// <param name="UsedBytes">Space currently used in bytes.</param>
/// <param name="TotalBytes">Total capacity in bytes.</param>
/// <param name="UsedPercent">Usage as a percentage (0–100).</param>
public sealed record DiskMetrics(long UsedBytes, long TotalBytes, double UsedPercent);
