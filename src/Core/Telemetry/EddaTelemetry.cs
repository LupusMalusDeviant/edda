using System.Diagnostics;
using System.Diagnostics.Metrics;
using Edda.Core.Abstractions;

namespace Edda.Core.Telemetry;

/// <summary>
/// BCL-backed <see cref="IEddaTelemetry"/> (D7): records to a <see cref="Meter"/> and an
/// <see cref="ActivitySource"/> both named <see cref="SourceName"/>. Recording is nearly free when no
/// listener/exporter is attached, so this is the always-on default; an opt-in OpenTelemetry exporter subscribes
/// to the same names (<c>AddMeter("Edda")</c> / <c>AddSource("Edda")</c>) without changing this class.
/// </summary>
public sealed class EddaTelemetry : IEddaTelemetry, IDisposable
{
    /// <summary>The metrics/trace source name; an exporter subscribes via <c>AddMeter</c>/<c>AddSource</c>.</summary>
    public const string SourceName = "Edda";

    private const string DurationInstrument = "edda.operation.duration";
    private const string TagOperation = "operation";
    private const string TagSuccess = "success";

    private readonly Meter _meter = new(SourceName);
    private readonly ActivitySource _activitySource = new(SourceName);
    private readonly Histogram<double> _duration;

    /// <summary>Initializes the meter, activity source and the operation-duration histogram.</summary>
    public EddaTelemetry()
        => _duration = _meter.CreateHistogram<double>(
            DurationInstrument, unit: "ms", description: "Duration of Edda operations by name and outcome.");

    /// <inheritdoc />
    public IDisposable? StartActivity(string operation) => _activitySource.StartActivity(operation);

    /// <inheritdoc />
    public void RecordDuration(string operation, double milliseconds, bool success)
        => _duration.Record(
            milliseconds,
            new KeyValuePair<string, object?>(TagOperation, operation),
            new KeyValuePair<string, object?>(TagSuccess, success));

    /// <summary>Disposes the underlying meter and activity source.</summary>
    public void Dispose()
    {
        _meter.Dispose();
        _activitySource.Dispose();
    }
}
