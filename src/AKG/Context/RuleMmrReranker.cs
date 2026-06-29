using Edda.Core.Models;

namespace Edda.AKG.Context;

/// <summary>
/// Maximal Marginal Relevance (MMR) reranker for scored knowledge rules. Diversifies the top
/// candidates by balancing relevance against similarity to already-selected rules, reducing
/// near-duplicate rules in the compiled context.
/// <para>
/// Relevance scores are normalized to [0,1] internally so the trade-off weight (<c>lambda</c>) is
/// meaningful regardless of the upstream score scale (e.g. small RRF scores vs. [0,1] cosine).
/// Rules without an embedding keep their relevance order and are appended after the diversified set.
/// </para>
/// Reimplemented here (rather than reusing <c>Memory.Hybrid.MmrReranker</c>) to respect the AKG
/// dependency rule (AKG → Core only).
/// </summary>
internal static class RuleMmrReranker
{
    /// <summary>
    /// Reranks <paramref name="ranked"/> via MMR over the supplied rule embeddings.
    /// </summary>
    /// <param name="ranked">Rules in descending relevance order (e.g. RRF-fused).</param>
    /// <param name="embeddings">Rule-ID → embedding vector, for candidates that have one.</param>
    /// <param name="k">Number of top rules to diversify.</param>
    /// <param name="lambda">Trade-off: 1.0 = pure relevance, 0.0 = pure diversity. Default 0.7.</param>
    /// <returns>
    /// Reranked rules: the diversified top-k first, then the remaining embedded rules, then rules
    /// without an embedding — both tails in their original relevance order.
    /// </returns>
    internal static IReadOnlyList<ScoredRule> Rerank(
        IReadOnlyList<ScoredRule> ranked,
        IReadOnlyDictionary<string, float[]> embeddings,
        int k,
        double lambda = 0.7)
    {
        var withEmbedding = ranked.Where(r => embeddings.ContainsKey(r.Rule.Id)).ToList();
        var withoutEmbedding = ranked.Where(r => !embeddings.ContainsKey(r.Rule.Id)).ToList();

        // Nothing to diversify with 0 or 1 embedded candidate.
        if (withEmbedding.Count <= 1)
            return ranked;

        // Normalize relevance to [0,1] so lambda balances it against the [0,1] cosine similarity.
        var maxScore = withEmbedding.Max(r => r.Score);
        var minScore = withEmbedding.Min(r => r.Score);
        var span = maxScore - minScore;
        double Relevance(ScoredRule r) => span > 0 ? (r.Score - minScore) / span : 1.0;

        var selected = new List<ScoredRule>();
        var remaining = withEmbedding.ToList();
        var limit = Math.Min(k, remaining.Count);

        while (selected.Count < limit && remaining.Count > 0)
        {
            ScoredRule? best = null;
            var bestMmr = double.NegativeInfinity;

            foreach (var candidate in remaining)
            {
                var maxSim = selected.Count == 0
                    ? 0.0
                    : selected.Max(s => CosineSimilarity(
                        embeddings[candidate.Rule.Id], embeddings[s.Rule.Id]));

                var mmr = lambda * Relevance(candidate) - (1 - lambda) * maxSim;
                if (mmr > bestMmr)
                {
                    bestMmr = mmr;
                    best = candidate;
                }
            }

            best ??= remaining[0];
            selected.Add(best);
            remaining.Remove(best);
        }

        return [.. selected, .. remaining, .. withoutEmbedding];
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
            return 0.0;

        double dot = 0.0, normA = 0.0, normB = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0.0 || normB == 0.0 ? 0.0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
