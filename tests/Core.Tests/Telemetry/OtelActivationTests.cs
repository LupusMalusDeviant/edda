using Edda.Core.Telemetry;

namespace Edda.Core.Tests.Telemetry;

/// <summary>
/// Unit tests for <see cref="OtelActivation"/> (D7): the OpenTelemetry exporter is opt-in — enabled only by
/// <c>EDDA_OTEL_ENABLED=true</c> or a non-empty <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, off by default.
/// </summary>
public sealed class OtelActivationTests
{
    [Theory]
    [InlineData("true", null, true)]
    [InlineData("TRUE", null, true)]
    [InlineData(null, "http://localhost:4317", true)]
    [InlineData("true", "http://localhost:4317", true)]
    [InlineData(null, null, false)]
    [InlineData("false", null, false)]
    [InlineData("1", null, false)]      // only the literal "true" enables the flag
    [InlineData(null, "   ", false)]    // a blank endpoint does not count as configured
    [InlineData("", "", false)]
    public void IsEnabled_ReflectsOptInSignals(string? flag, string? endpoint, bool expected)
        => OtelActivation.IsEnabled(flag, endpoint).Should().Be(expected);
}
