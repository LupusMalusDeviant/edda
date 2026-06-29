using System.Text.Json.Serialization;

namespace Edda.Agent.Tdk.Models;

/// <summary>
/// The JSON payload that a TDK validator script writes to stdout.
/// Parsed by <see cref="TdkEngine"/> after sandbox execution.
/// </summary>
internal sealed class TdkValidatorOutput
{
    /// <summary>
    /// <see langword="true"/> when the code block satisfies the rule; <see langword="false"/> when violations were found.
    /// </summary>
    [JsonPropertyName("pass")]
    public required bool Pass { get; init; }

    /// <summary>Violations detected by the validator. Empty when <see cref="Pass"/> is <see langword="true"/>.</summary>
    [JsonPropertyName("violations")]
    public IReadOnlyList<TdkValidatorViolation> Violations { get; init; } = [];
}

/// <summary>
/// A single violation entry as reported by a TDK validator script.
/// Contains more detail than <see cref="Edda.Core.Models.TdkViolation"/>
/// to enable richer feedback prompts.
/// </summary>
internal sealed class TdkValidatorViolation
{
    /// <summary>The AKG rule ID that was violated.</summary>
    [JsonPropertyName("rule_id")]
    public required string RuleId { get; init; }

    /// <summary>Human-readable description of the violation.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Severity level: "error", "warning", or "info".</summary>
    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    /// <summary>Optional source line number in the code block where the violation was detected.</summary>
    [JsonPropertyName("line")]
    public int? Line { get; init; }

    /// <summary>Optional suggestion for how to fix the violation.</summary>
    [JsonPropertyName("suggestion")]
    public string? Suggestion { get; init; }
}
