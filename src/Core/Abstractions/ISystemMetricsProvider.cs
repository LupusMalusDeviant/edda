using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Platform abstraction for reading system resource metrics.
/// Implementations: LinuxMetricsProvider (/proc), WindowsMetricsProvider (PerformanceCounter),
/// MockMetricsProvider (unit tests).
/// </summary>
public interface ISystemMetricsProvider
{
    /// <summary>
    /// Returns a point-in-time snapshot of system resource usage.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current CPU, memory, and disk metrics.</returns>
    Task<SystemMetrics> GetCurrentAsync(CancellationToken ct = default);
}
