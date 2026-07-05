using System.Diagnostics;
using System.Diagnostics.Metrics;
using Edda.Core.Telemetry;

namespace Edda.Core.Tests.Telemetry;

/// <summary>
/// Unit tests for <see cref="EddaTelemetry"/> (D7): a duration measurement reaches the "Edda" meter with the
/// operation/success tags, and a span is created on the "Edda" activity source when a listener is active. Tests
/// use unique operation names so they stay robust under parallel execution (the meter/source are process-global).
/// </summary>
public sealed class EddaTelemetryTests
{
    [Fact]
    public void RecordDuration_EmitsHistogramMeasurement_WithOperationAndSuccessTags()
    {
        var captured = new List<(double Value, string? Operation, bool Success)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == EddaTelemetry.SourceName)
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            string? op = null;
            var success = false;
            foreach (var tag in tags)
            {
                if (tag.Key == "operation") op = tag.Value as string;
                else if (tag.Key == "success") success = tag.Value is true;
            }
            captured.Add((value, op, success));
        });
        listener.Start();

        using var telemetry = new EddaTelemetry();
        telemetry.RecordDuration("edda-telemetry-test-op", 42.5, success: true);

        listener.Dispose(); // flush any pending measurements

        captured.Should().Contain(m => m.Operation == "edda-telemetry-test-op" && m.Value == 42.5 && m.Success);
    }

    [Fact]
    public void StartActivity_CreatesActivity_WithOperationName_WhenListenerActive()
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EddaTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (activity.OperationName == "edda-telemetry-test-span") captured = activity;
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var telemetry = new EddaTelemetry();
        using (telemetry.StartActivity("edda-telemetry-test-span")) { }

        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("edda-telemetry-test-span");
    }
}
