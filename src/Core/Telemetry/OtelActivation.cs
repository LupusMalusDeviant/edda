namespace Edda.Core.Telemetry;

/// <summary>
/// Decides whether the OpenTelemetry exporter should be activated (D7, Slice 2). Opt-in and default-off to keep
/// the local-first / zero-infra default: it activates only when explicitly enabled (<c>EDDA_OTEL_ENABLED=true</c>)
/// or when a standard OTLP endpoint is configured (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>). When neither is present no
/// OpenTelemetry pipeline is built, so there is no exporter and no runtime overhead.
/// </summary>
public static class OtelActivation
{
    /// <summary>Whether the OpenTelemetry export pipeline should be wired up.</summary>
    /// <param name="enabledFlag">The <c>EDDA_OTEL_ENABLED</c> configuration value.</param>
    /// <param name="otlpEndpoint">The <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> configuration value.</param>
    /// <returns><see langword="true"/> when either opt-in signal is present.</returns>
    public static bool IsEnabled(string? enabledFlag, string? otlpEndpoint)
        => string.Equals(enabledFlag, "true", StringComparison.OrdinalIgnoreCase)
           || !string.IsNullOrWhiteSpace(otlpEndpoint);
}
