using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Context;

/// <summary>
/// Hybrid-retrieval reranker: fuses keyword and semantic rankings via Reciprocal Rank Fusion (RRF)
/// and diversifies the top candidates via Maximal Marginal Relevance (MMR).
/// <para>
/// Semantic scores come from the native Neo4j vector index (<c>db.index.vector.queryNodes</c> on
/// <c>chunk_embeddings</c> over <c>(:RuleChunk)</c>, mapped back to the parent document) with an app-side
/// cosine fallback when the index is missing or the graph provider does not support vector indexes (e.g.
/// Memgraph). RRF is rank-based and robust to
/// score-scale differences; MMR reduces near-duplicate rules in the compiled context.
/// </para>
/// Returns the keyword-scored rules unchanged when embeddings are unavailable or no rule meets the
/// similarity threshold — no regression versus pure keyword scoring.
/// </summary>
internal sealed class SemanticBooster
{
    /// <summary>Name of the Neo4j vector index over <c>(:RuleChunk).embedding</c>.</summary>
    private const string VectorIndexName = "chunk_embeddings";

    /// <summary>RRF dampening constant (standard value 60).</summary>
    private const int RrfK = 60;

    private readonly RetrievalOptions _options;
    private readonly IEmbeddingService _embeddings;
    private readonly ICypherExecutor _cypher;
    private readonly ILogger<SemanticBooster> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="SemanticBooster"/>.
    /// </summary>
    /// <param name="embeddings">Embedding service for generating query vectors.</param>
    /// <param name="cypher">Cypher executor for the vector index query and embedding retrieval.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="options">
    /// Tunable retrieval thresholds/limits (similarity threshold, vector top-K, MMR top-N and lambda).
    /// Null uses the defaults, which preserve the historical hard-coded behaviour.
    /// </param>
    internal SemanticBooster(
        IEmbeddingService embeddings,
        ICypherExecutor cypher,
        ILogger<SemanticBooster> logger,
        RetrievalOptions? options = null)
    {
        _embeddings = embeddings;
        _cypher = cypher;
        _logger = logger;
        _options = options ?? new RetrievalOptions();
    }

    /// <summary>
    /// Reranks the keyword-scored rules by fusing them with semantic similarity (RRF) and
    /// diversifying the top candidates (MMR). Returns the input unchanged when the embedding service
    /// is unavailable or no rule reaches the similarity threshold.
    /// </summary>
    /// <param name="scoredRules">Rules with keyword scores (already feedback-adjusted), highest first.</param>
    /// <param name="context">Task context providing the query text and user scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="precomputedQueryEmbedding">
    /// Optional query embedding already computed by the caller (stage 0). When provided it is reused;
    /// when null the booster embeds the query text itself (legacy/standalone behaviour).
    /// </param>
    /// <returns>Reranked rules in descending relevance order.</returns>
    internal async Task<IReadOnlyList<ScoredRule>> BoostAsync(
        IReadOnlyList<ScoredRule> scoredRules,
        TaskContext context,
        CancellationToken ct,
        float[]? precomputedQueryEmbedding = null)
    {
        if (!_embeddings.IsAvailable || scoredRules.Count == 0)
            return scoredRules;

        float[] queryEmbedding;
        if (precomputedQueryEmbedding is not null)
        {
            // Reuse the query vector already computed for stage-1 head pre-pruning — avoids a second embed.
            queryEmbedding = precomputedQueryEmbedding;
        }
        else
        {
            try
            {
                queryEmbedding = await _embeddings.EmbedAsync(context.Task, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute query embedding; skipping semantic phase | {Component}", "AKG");
                return scoredRules;
            }
        }

        if (queryEmbedding.Length == 0)
            return scoredRules;

        var candidateIds = scoredRules.Select(r => r.Rule.Id).ToHashSet(StringComparer.Ordinal);
        var semanticScores = await ComputeSemanticScoresAsync(queryEmbedding, candidateIds, context.UserId, ct)
            .ConfigureAwait(false);

        // No semantic signal → keep the keyword ranking unchanged (no regression).
        if (semanticScores.Count == 0)
            return scoredRules;

        // Fuse keyword and semantic rankings via RRF (rank-based, scale-robust).
        var fused = RrfFuse(scoredRules, semanticScores);

        // Diversify the top candidates via MMR over their embeddings.
        var topIds = fused.Take(_options.MmrTopN).Select(r => r.Rule.Id).ToList();
        var topEmbeddings = await FetchRepresentativeEmbeddingsAsync(topIds, ct).ConfigureAwait(false);
        if (topEmbeddings.Count > 1)
            fused = RuleMmrReranker.Rerank(fused, topEmbeddings, _options.MmrTopN, _options.MmrLambda);

        _logger.LogDebug(
            "Semantic phase: RRF over {Sem} matches, MMR over {Emb} embedded top candidates | {Component}",
            semanticScores.Count, topEmbeddings.Count, "AKG");

        return fused;
    }

    // ── RRF fusion ───────────────────────────────────────────────────────────

    /// <summary>
    /// Fuses the keyword ranking (input order, by score) with the semantic ranking (by cosine) using
    /// Reciprocal Rank Fusion with tie-aware (dense) ranks. Rewrites each rule's score to its RRF
    /// value and returns the list re-sorted by descending RRF score.
    /// </summary>
    private static IReadOnlyList<ScoredRule> RrfFuse(
        IReadOnlyList<ScoredRule> keywordRanked,
        IReadOnlyDictionary<string, double> semanticScores)
    {
        var keywordRanks = DenseRanks(keywordRanked.Select(r => r.Score).ToList());
        var keywordRankById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < keywordRanked.Count; i++)
            keywordRankById[keywordRanked[i].Rule.Id] = keywordRanks[i];

        var semanticSorted = semanticScores.OrderByDescending(kv => kv.Value).ToList();
        var semanticRanks = DenseRanks(semanticSorted.Select(kv => kv.Value).ToList());
        var semanticRankById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < semanticSorted.Count; i++)
            semanticRankById[semanticSorted[i].Key] = semanticRanks[i];

        var fused = keywordRanked.ToList();
        foreach (var scoredRule in fused)
        {
            var rrf = 1.0 / (RrfK + keywordRankById[scoredRule.Rule.Id]);
            if (semanticRankById.TryGetValue(scoredRule.Rule.Id, out var semanticRank))
                rrf += 1.0 / (RrfK + semanticRank);
            scoredRule.Score = rrf;
        }

        fused.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return fused;
    }

    /// <summary>
    /// Assigns 1-based dense ranks to a descending-sorted value list: equal values share a rank.
    /// </summary>
    private static int[] DenseRanks(IReadOnlyList<double> descendingValues)
    {
        var ranks = new int[descendingValues.Count];
        var rank = 0;
        double? previous = null;
        for (var i = 0; i < descendingValues.Count; i++)
        {
            if (previous is null || descendingValues[i] != previous.Value)
                rank++;
            ranks[i] = rank;
            previous = descendingValues[i];
        }

        return ranks;
    }

    // ── Semantic score retrieval (vector index + app-side fallback) ──────────

    private async Task<IReadOnlyDictionary<string, double>> ComputeSemanticScoresAsync(
        float[] queryEmbedding,
        IReadOnlySet<string> candidateIds,
        string? userId,
        CancellationToken ct)
    {
        try
        {
            var rows = await _cypher.QueryAsync(
                """
                CALL db.index.vector.queryNodes($index, $topK, $vector)
                YIELD node, score
                WHERE score > $threshold
                  AND (node.ownerId IS NULL OR node.ownerId = $userId)
                RETURN node.parentId AS id, max(score) AS score
                """,
                new
                {
                    index = VectorIndexName,
                    topK = _options.VectorTopK,
                    vector = queryEmbedding,
                    threshold = _options.SimilarityThreshold,
                    userId,
                },
                ct).ConfigureAwait(false);

            // Index query succeeded — trust its result (empty = nothing above threshold).
            var scores = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var id = row.TryGetValue("id", out var i) ? i?.ToString() : null;
                if (id is null || !candidateIds.Contains(id)) continue;
                if (row.TryGetValue("score", out var sc) && sc is not null)
                {
                    var value = Convert.ToDouble(sc);
                    scores[id] = scores.TryGetValue(id, out var existing) ? Math.Max(existing, value) : value;
                }
            }

            return scores;
        }
        catch (Exception ex)
        {
            // Index missing or unsupported (e.g. Memgraph) → app-side cosine fallback.
            _logger.LogDebug(ex,
                "Vector index '{Index}' unavailable; using app-side cosine fallback | {Component}",
                VectorIndexName, "AKG");
            return await ComputeAppSideScoresAsync(queryEmbedding, candidateIds, ct).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyDictionary<string, double>> ComputeAppSideScoresAsync(
        float[] queryEmbedding,
        IReadOnlySet<string> candidateIds,
        CancellationToken ct)
    {
        var byRule = await FetchChunkEmbeddingsAsync(candidateIds, ct).ConfigureAwait(false);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, chunkEmbeddings) in byRule)
        {
            var best = 0.0;
            foreach (var embedding in chunkEmbeddings)
                best = Math.Max(best, CosineSimilarity(queryEmbedding, embedding));
            if (best > _options.SimilarityThreshold)
                scores[id] = best;
        }

        return scores;
    }

    // ── Embedding retrieval (chunk-keyed, mapped back to the parent document) ──

    /// <summary>
    /// Loads one representative chunk embedding (lowest ordinal) per rule, used by MMR to diversify the
    /// top candidates at the document level.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, float[]>> FetchRepresentativeEmbeddingsAsync(
        IEnumerable<string> ruleIds, CancellationToken ct)
    {
        var ids = ruleIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<string, float[]>();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = await _cypher.QueryAsync(
                """
                MATCH (r:Rule)-[:HAS_CHUNK]->(c:RuleChunk)
                WHERE r.id IN $ids
                WITH r, c ORDER BY c.ord
                RETURN r.id AS id, collect(c.embedding)[0] AS emb
                """,
                new { ids },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve representative chunk embeddings | {Component}", "AKG");
            return new Dictionary<string, float[]>();
        }

        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!row.ContainsKey("id") || row["id"] is null) continue;
            var id = row["id"]!.ToString()!;
            var embedding = ToFloatArray(row.TryGetValue("emb", out var e) ? e : null);
            if (embedding.Length > 0)
                result[id] = embedding;
        }

        return result;
    }

    /// <summary>Loads all chunk embeddings grouped by parent rule id (for the app-side cosine fallback).</summary>
    private async Task<IReadOnlyDictionary<string, List<float[]>>> FetchChunkEmbeddingsAsync(
        IEnumerable<string> ruleIds, CancellationToken ct)
    {
        var ids = ruleIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<string, List<float[]>>();

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = await _cypher.QueryAsync(
                """
                MATCH (r:Rule)-[:HAS_CHUNK]->(c:RuleChunk)
                WHERE r.id IN $ids
                RETURN r.id AS id, collect(c.embedding) AS embs
                """,
                new { ids },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve chunk embeddings | {Component}", "AKG");
            return new Dictionary<string, List<float[]>>();
        }

        var result = new Dictionary<string, List<float[]>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!row.ContainsKey("id") || row["id"] is null) continue;
            var id = row["id"]!.ToString()!;
            var list = ToFloatArrayList(row.TryGetValue("embs", out var e) ? e : null);
            if (list.Count > 0)
                result[id] = list;
        }

        return result;
    }

    private static float[] ToFloatArray(object? value)
        => value is IEnumerable<object> list ? list.Select(Convert.ToSingle).ToArray() : [];

    private static List<float[]> ToFloatArrayList(object? value)
    {
        var result = new List<float[]>();
        if (value is IEnumerable<object> outer)
        {
            foreach (var item in outer)
            {
                var array = ToFloatArray(item);
                if (array.Length > 0)
                    result.Add(array);
            }
        }

        return result;
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
