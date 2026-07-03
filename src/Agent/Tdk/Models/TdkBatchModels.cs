using System.Text.Json.Serialization;

namespace Edda.Agent.Tdk.Models;

/// <summary>The batch request written to the F11 batch runner via stdin.</summary>
internal sealed class TdkBatchRequest
{
    /// <summary>The validator jobs to run in one sandbox.</summary>
    [JsonPropertyName("jobs")]
    public required IReadOnlyList<TdkBatchJob> Jobs { get; init; }
}

/// <summary>A single validator job in a batch.</summary>
internal sealed class TdkBatchJob
{
    /// <summary>Zero-based job index; used to map the result back to its (rule × block).</summary>
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    /// <summary>The validator script to run.</summary>
    [JsonPropertyName("script")]
    public required string Script { get; init; }

    /// <summary>The validator input passed to the job on stdin.</summary>
    [JsonPropertyName("input")]
    public required TdkValidatorInput Input { get; init; }
}

/// <summary>The batch response the runner writes to stdout.</summary>
internal sealed class TdkBatchResponse
{
    /// <summary>Per-job results.</summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<TdkBatchJobResult> Results { get; init; } = [];
}

/// <summary>The result of one validator job in a batch.</summary>
internal sealed class TdkBatchJobResult
{
    /// <summary>The job index this result belongs to.</summary>
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    /// <summary>The validator process exit code.</summary>
    [JsonPropertyName("exit_code")]
    public required int ExitCode { get; init; }

    /// <summary>Captured standard output (the validator's <c>{pass, violations}</c> document).</summary>
    [JsonPropertyName("stdout")]
    public string Stdout { get; init; } = "";

    /// <summary>Captured standard error.</summary>
    [JsonPropertyName("stderr")]
    public string Stderr { get; init; } = "";

    /// <summary>True when the validator exceeded its per-job time budget.</summary>
    [JsonPropertyName("timed_out")]
    public bool TimedOut { get; init; }
}
