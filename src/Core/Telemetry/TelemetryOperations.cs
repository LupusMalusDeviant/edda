namespace Edda.Core.Telemetry;

/// <summary>
/// The fixed set of operation names used with <see cref="Edda.Core.Abstractions.IEddaTelemetry"/> (D7). Kept as
/// constants so metric tag values are bounded and consistent — never pass user-derived strings.
/// </summary>
public static class TelemetryOperations
{
    /// <summary>Context compilation (the retrieval pipeline: keyword → semantic → expand → format).</summary>
    public const string ContextCompilation = "context_compilation";
}
