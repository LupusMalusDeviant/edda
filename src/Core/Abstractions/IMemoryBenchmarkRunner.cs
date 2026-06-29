using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Runs a retrieval benchmark over the hybrid short-term-memory search path
/// (<see cref="IMemorySearch"/>), scoring the retrieved memory entries against curated ground-truth
/// expectations. Mirrors <see cref="IBenchmarkRunner"/> but measures memory-over-history retrieval —
/// the task that LongMemEval/LoCoMo evaluate — rather than AKG rule retrieval.
/// </summary>
/// <remarks>
/// For memory cases, <see cref="BenchmarkCase.ExpectedRuleIds"/> holds the expected
/// <see cref="ShortTermMemoryEntry.Id"/> values (the gold evidence entries), and
/// <see cref="BenchmarkCase.UserId"/> identifies the memory scope to search.
/// </remarks>
public interface IMemoryBenchmarkRunner
{
    /// <summary>
    /// Runs every case in <paramref name="dataset"/> through <see cref="IMemorySearch.SearchAsync"/>
    /// and scores the retrieved entry IDs against each case's expected IDs at rank <paramref name="k"/>.
    /// </summary>
    /// <param name="dataset">The cases to evaluate.</param>
    /// <param name="k">Cutoff rank for all @k metrics. Non-positive values fall back to a default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A report with per-case results, token estimates, and the dataset-level aggregate.</returns>
    Task<BenchmarkReport> RunAsync(
        BenchmarkDataset dataset,
        int k = 10,
        CancellationToken cancellationToken = default);
}
