using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Runs a retrieval benchmark over the AKG context-compilation pipeline, scoring the rules it surfaces
/// against curated ground-truth expectations. Produces standard IR metrics (Recall@k, Precision@k,
/// MRR, nDCG@k) plus latency percentiles.
/// </summary>
public interface IBenchmarkRunner
{
    /// <summary>
    /// Runs every case in <paramref name="dataset"/> through
    /// <see cref="IKnowledgeGraph.CompileContextAsync"/> and scores the retrieved active rules against
    /// each case's expected rule IDs at rank <paramref name="k"/>.
    /// </summary>
    /// <param name="dataset">The cases to evaluate.</param>
    /// <param name="k">Cutoff rank for all @k metrics. Non-positive values fall back to a default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A report with per-case results and the dataset-level aggregate.</returns>
    Task<BenchmarkReport> RunAsync(
        BenchmarkDataset dataset,
        int k = 10,
        CancellationToken cancellationToken = default);
}
