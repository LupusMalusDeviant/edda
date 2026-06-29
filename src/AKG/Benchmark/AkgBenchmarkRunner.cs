using Edda.Core.Abstractions;
using Edda.Core.Benchmark;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Benchmark;

/// <summary>
/// Default <see cref="IBenchmarkRunner"/>: evaluates retrieval quality by running each case through the
/// AKG context-compilation pipeline and scoring the surfaced active rules against ground truth.
/// </summary>
public sealed class AkgBenchmarkRunner : IBenchmarkRunner
{
    private const int DefaultK = 10;

    private readonly IKnowledgeGraph _graph;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AkgBenchmarkRunner> _logger;

    /// <summary>Initializes a new runner.</summary>
    /// <param name="graph">The knowledge graph whose context compilation is measured.</param>
    /// <param name="timeProvider">Time source for latency measurement (Regel 4).</param>
    /// <param name="logger">Logger for run diagnostics.</param>
    public AkgBenchmarkRunner(
        IKnowledgeGraph graph, TimeProvider timeProvider, ILogger<AkgBenchmarkRunner> logger)
    {
        _graph = graph;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BenchmarkReport> RunAsync(
        BenchmarkDataset dataset, int k = DefaultK, CancellationToken cancellationToken = default)
    {
        if (k <= 0)
        {
            k = DefaultK;
        }

        _logger.LogInformation(
            "Benchmark started: dataset={Dataset}, cases={Cases}, k={K} | {Component}",
            dataset.Name, dataset.Cases.Count, k, "AKG");

        var results = new List<BenchmarkCaseResult>(dataset.Cases.Count);
        var latencies = new List<double>(dataset.Cases.Count);

        foreach (var benchmarkCase in dataset.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (benchmarkCase.ExpectedRuleIds.Count == 0)
            {
                _logger.LogWarning(
                    "Benchmark case has no expected rule IDs; metrics will be zero | case={Case} | {Component}",
                    benchmarkCase.Id, "AKG");
            }

            var taskContext = new TaskContext
            {
                Task = benchmarkCase.Query,
                Concepts = benchmarkCase.Concepts,
                UserId = benchmarkCase.UserId,
            };

            long start = _timeProvider.GetTimestamp();
            var context = await _graph.CompileContextAsync(taskContext, cancellationToken).ConfigureAwait(false);
            double latencyMs = _timeProvider.GetElapsedTime(start).TotalMilliseconds;

            var ranked = context.ActiveRules.Select(r => r.Id).ToList();
            var metrics = RetrievalMetrics.Compute(ranked, benchmarkCase.ExpectedRuleIds, k);
            var estimatedTokens = TokenEstimator.Estimate(context.FormattedContext);

            latencies.Add(latencyMs);
            results.Add(new BenchmarkCaseResult
            {
                CaseId = benchmarkCase.Id,
                Metrics = metrics,
                RetrievedRuleIds = ranked,
                LatencyMs = latencyMs,
                EstimatedTokens = estimatedTokens,
            });
        }

        var aggregate = Aggregate(results);

        _logger.LogInformation(
            "Benchmark finished: dataset={Dataset}, cases={Cases}, recall@{K}={Recall:F3} | {Component}",
            dataset.Name, results.Count, k, aggregate.RecallAtK, "AKG");

        return new BenchmarkReport
        {
            DatasetName = dataset.Name,
            K = k,
            CaseCount = results.Count,
            Aggregate = aggregate,
            LatencyMsP50 = RetrievalMetrics.Percentile(latencies, 50),
            LatencyMsP95 = RetrievalMetrics.Percentile(latencies, 95),
            AvgEstimatedTokens = results.Count == 0 ? 0 : results.Average(r => (double)r.EstimatedTokens),
            Cases = results,
        };
    }

    /// <summary>Means each retrieval metric across all case results.</summary>
    private static BenchmarkMetrics Aggregate(IReadOnlyList<BenchmarkCaseResult> results)
    {
        if (results.Count == 0)
        {
            return new BenchmarkMetrics();
        }

        return new BenchmarkMetrics
        {
            RecallAtK = results.Average(r => r.Metrics.RecallAtK),
            PrecisionAtK = results.Average(r => r.Metrics.PrecisionAtK),
            Mrr = results.Average(r => r.Metrics.Mrr),
            NdcgAtK = results.Average(r => r.Metrics.NdcgAtK),
        };
    }
}
