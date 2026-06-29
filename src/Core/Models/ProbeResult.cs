namespace Edda.Core.Models;

/// <summary>
/// Result of probing an embedding or LLM provider endpoint: whether it is reachable, a human-readable
/// message for the UI, and the model identifiers the source reports as available (empty when the provider
/// does not support listing).
/// </summary>
public sealed record ProbeResult
{
    /// <summary>Whether the endpoint was reachable and responded successfully.</summary>
    public required bool Ok { get; init; }

    /// <summary>Human-readable status message (success summary or error reason).</summary>
    public required string Message { get; init; }

    /// <summary>Model identifiers discovered at the source; empty if listing is unsupported or failed.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];
}
