namespace Edda.Core.Abstractions;

/// <summary>
/// Application telemetry facade (D7): records metrics and trace spans for Edda's own operations via the BCL
/// <c>System.Diagnostics.Metrics</c> / <c>System.Diagnostics.Activity</c> primitives. Recording is cheap and
/// always on; an OpenTelemetry exporter is wired separately and opt-in, so the default build stays local-first
/// with no external dependency. Operation names are a fixed, closed set (<c>TelemetryOperations</c>) so metric
/// tag cardinality stays bounded — never pass user-derived strings.
/// </summary>
public interface IEddaTelemetry
{
    /// <summary>
    /// Starts a trace span for the operation; dispose the returned scope to end it. Returns
    /// <see langword="null"/> when no listener/exporter is active (the always-on default), which callers can
    /// safely wrap in a <c>using</c> (disposing null is a no-op).
    /// </summary>
    /// <param name="operation">A fixed operation name (see <c>TelemetryOperations</c>).</param>
    /// <returns>A disposable span scope, or <see langword="null"/> when tracing is inactive.</returns>
    IDisposable? StartActivity(string operation);

    /// <summary>Records the wall-clock duration of a completed operation, tagged with the operation and outcome.</summary>
    /// <param name="operation">A fixed operation name (see <c>TelemetryOperations</c>).</param>
    /// <param name="milliseconds">Elapsed time in milliseconds.</param>
    /// <param name="success">Whether the operation completed without throwing.</param>
    void RecordDuration(string operation, double milliseconds, bool success);
}
