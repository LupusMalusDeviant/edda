using System.Text.Json.Serialization;

namespace Edda.Agent.Tdk.Models;

/// <summary>
/// The JSON payload passed to a TDK validator script via stdin.
/// The validator reads this from stdin and uses it to evaluate the code block.
/// </summary>
internal sealed class TdkValidatorInput
{
    /// <summary>The code block extracted from the LLM response.</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>The language identifier of the code block (e.g. "python", "csharp").</summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>The AKG rule ID whose validator is being executed.</summary>
    [JsonPropertyName("rule_id")]
    public required string RuleId { get; init; }

    /// <summary>The original user message that triggered the agent turn.</summary>
    [JsonPropertyName("user_message")]
    public required string UserMessage { get; init; }
}
