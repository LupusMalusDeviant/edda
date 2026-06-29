namespace Edda.Core.Models;

/// <summary>
/// A single benchmark case: a query and the rule IDs that an ideal retrieval should surface for it.
/// </summary>
public sealed record BenchmarkCase
{
    /// <summary>Stable identifier for the case, echoed into the report.</summary>
    public required string Id { get; init; }

    /// <summary>The query text submitted to context compilation.</summary>
    public required string Query { get; init; }

    /// <summary>Ground-truth rule IDs. Should contain at least one ID; an empty set scores all metrics as zero.</summary>
    public IReadOnlyList<string> ExpectedRuleIds { get; init; } = [];

    /// <summary>Optional pre-extracted concepts passed through to the task context.</summary>
    public IReadOnlyList<string> Concepts { get; init; } = [];

    /// <summary>Optional user scope. Null restricts retrieval to global rules.</summary>
    public string? UserId { get; init; }
}

/// <summary>A named collection of benchmark cases.</summary>
public sealed record BenchmarkDataset
{
    /// <summary>Human-readable dataset name, echoed into the report.</summary>
    public required string Name { get; init; }

    /// <summary>The cases to evaluate.</summary>
    public IReadOnlyList<BenchmarkCase> Cases { get; init; } = [];
}

/// <summary>
/// Retrieval-quality metrics at a fixed cutoff k. Used both per-case and as the dataset-level mean.
/// </summary>
public sealed record BenchmarkMetrics
{
    /// <summary>Fraction of expected rules found within the top-k (hits / |expected|).</summary>
    public double RecallAtK { get; init; }

    /// <summary>Fraction of the top-k slots that are relevant (hits / k).</summary>
    public double PrecisionAtK { get; init; }

    /// <summary>Mean reciprocal rank: 1 / position of the first relevant hit within the top-k, else 0.</summary>
    public double Mrr { get; init; }

    /// <summary>Normalized discounted cumulative gain at k (binary relevance), in [0, 1].</summary>
    public double NdcgAtK { get; init; }
}

/// <summary>Per-case benchmark outcome.</summary>
public sealed record BenchmarkCaseResult
{
    /// <summary>The originating case ID.</summary>
    public required string CaseId { get; init; }

    /// <summary>Retrieval metrics for this case.</summary>
    public required BenchmarkMetrics Metrics { get; init; }

    /// <summary>The rule IDs returned by context compilation, in rank order.</summary>
    public IReadOnlyList<string> RetrievedRuleIds { get; init; } = [];

    /// <summary>Wall-clock latency of the context compilation for this case, in milliseconds.</summary>
    public double LatencyMs { get; init; }

    /// <summary>
    /// Estimated token count of the retrieved context for this case (heuristic, ≈ chars / 4).
    /// Enables tokens-per-query comparison against systems like Mem0. Zero when not measured.
    /// </summary>
    public int EstimatedTokens { get; init; }
}

/// <summary>Full benchmark report for a dataset run.</summary>
public sealed record BenchmarkReport
{
    /// <summary>The evaluated dataset's name.</summary>
    public required string DatasetName { get; init; }

    /// <summary>The cutoff rank k used for all @k metrics.</summary>
    public int K { get; init; }

    /// <summary>Number of cases evaluated.</summary>
    public int CaseCount { get; init; }

    /// <summary>Mean of each retrieval metric across all cases.</summary>
    public required BenchmarkMetrics Aggregate { get; init; }

    /// <summary>Median (50th percentile) per-case latency in milliseconds.</summary>
    public double LatencyMsP50 { get; init; }

    /// <summary>95th-percentile per-case latency in milliseconds.</summary>
    public double LatencyMsP95 { get; init; }

    /// <summary>
    /// Mean estimated token count of the retrieved context per case (heuristic, ≈ chars / 4).
    /// The tokens-per-query efficiency figure comparable to published memory-system benchmarks.
    /// </summary>
    public double AvgEstimatedTokens { get; init; }

    /// <summary>Per-case results in dataset order.</summary>
    public IReadOnlyList<BenchmarkCaseResult> Cases { get; init; } = [];
}
