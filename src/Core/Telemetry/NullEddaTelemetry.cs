using Edda.Core.Abstractions;

namespace Edda.Core.Telemetry;

/// <summary>
/// No-op <see cref="IEddaTelemetry"/> (D7): the behaviour-neutral fallback used in tests and by components that
/// receive no telemetry dependency. Records nothing and starts no spans.
/// </summary>
public sealed class NullEddaTelemetry : IEddaTelemetry
{
    /// <summary>The shared singleton instance.</summary>
    public static NullEddaTelemetry Instance { get; } = new();

    /// <inheritdoc />
    public IDisposable? StartActivity(string operation) => null;

    /// <inheritdoc />
    public void RecordDuration(string operation, double milliseconds, bool success)
    {
        // Intentionally empty: the null object records nothing.
    }
}
