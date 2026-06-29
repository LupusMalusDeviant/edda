using Edda.Core.Models;

namespace Edda.Core.Benchmark;

/// <summary>
/// Pure, infrastructure-free computation of retrieval-quality metrics. Deterministic and side-effect free,
/// so it is fully unit-testable without any graph, memory, or embedding backend.
/// <para>
/// Shared by all benchmark runners (AKG rule retrieval and hybrid memory retrieval) so the metric
/// definitions stay identical and comparable.
/// </para>
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>
    /// Computes Recall@k, Precision@k, MRR and nDCG@k for one ranked result against a ground-truth set.
    /// </summary>
    /// <param name="ranked">Retrieved IDs in rank order (best first).</param>
    /// <param name="expected">Ground-truth relevant IDs.</param>
    /// <param name="k">Cutoff rank.</param>
    /// <returns>The metrics. All zero when <paramref name="expected"/> is empty or <paramref name="k"/> is non-positive.</returns>
    public static BenchmarkMetrics Compute(
        IReadOnlyList<string> ranked, IReadOnlyList<string> expected, int k)
    {
        if (k <= 0 || expected.Count == 0)
        {
            return new BenchmarkMetrics();
        }

        var relevant = new HashSet<string>(expected, StringComparer.Ordinal);
        var topK = ranked.Take(k).ToList();

        int hits = topK.Count(relevant.Contains);

        double recall = (double)hits / relevant.Count;
        double precision = (double)hits / k;

        double mrr = 0.0;
        for (int i = 0; i < topK.Count; i++)
        {
            if (relevant.Contains(topK[i]))
            {
                mrr = 1.0 / (i + 1);
                break;
            }
        }

        // DCG with binary relevance: position p = i + 1 (1-based), discount = log2(p + 1) = log2(i + 2).
        double dcg = 0.0;
        for (int i = 0; i < topK.Count; i++)
        {
            if (relevant.Contains(topK[i]))
            {
                dcg += 1.0 / Math.Log2(i + 2);
            }
        }

        double idcg = 0.0;
        int idealHits = Math.Min(k, relevant.Count);
        for (int i = 0; i < idealHits; i++)
        {
            idcg += 1.0 / Math.Log2(i + 2);
        }

        double ndcg = idcg > 0.0 ? dcg / idcg : 0.0;

        return new BenchmarkMetrics
        {
            RecallAtK = recall,
            PrecisionAtK = precision,
            Mrr = mrr,
            NdcgAtK = ndcg,
        };
    }

    /// <summary>
    /// Nearest-rank percentile of a set of values. Sorts internally; returns 0 for an empty input.
    /// </summary>
    /// <param name="values">The sample values (any order).</param>
    /// <param name="percentile">Percentile in [0, 100].</param>
    /// <returns>The value at the nearest rank, or 0 when <paramref name="values"/> is empty.</returns>
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }

        var sorted = values.OrderBy(v => v).ToList();
        int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count);
        rank = Math.Clamp(rank, 1, sorted.Count);
        return sorted[rank - 1];
    }
}
