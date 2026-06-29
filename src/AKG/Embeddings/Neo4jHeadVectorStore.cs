using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Embeddings;

/// <summary>
/// Neo4j-backed <see cref="IHeadVectorStore"/>. Derives, per repository / upload head, a small set of
/// centroids from the head's descendant chunk embeddings (k-means, see <see cref="KMeans"/>) and stores
/// them as <c>(:HeadVector)</c> nodes behind a dedicated <c>head_embeddings</c> vector index. Stage 1 of
/// hierarchical retrieval queries this index to pre-prune to the most relevant subtrees (ADR-0009).
/// </summary>
internal sealed class Neo4jHeadVectorStore : IHeadVectorStore
{
    /// <summary>Name of the Neo4j vector index over <c>(:HeadVector).embedding</c>.</summary>
    private const string IndexName = "head_embeddings";

    /// <summary>Roughly one centroid per this many chunks, capped by <see cref="MaxCentroids"/>.</summary>
    private const int ChunksPerCentroid = 50;

    /// <summary>Upper bound on centroids per head (Multi-Centroid against blurry broad-repo averages).</summary>
    private const int MaxCentroids = 8;

    private readonly ICypherExecutor _cypher;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<Neo4jHeadVectorStore> _logger;

    /// <summary>Initializes a new instance of <see cref="Neo4jHeadVectorStore"/>.</summary>
    /// <param name="cypher">Cypher executor for graph read/write.</param>
    /// <param name="embeddings">Embedding service (for dimensions / availability).</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public Neo4jHeadVectorStore(
        ICypherExecutor cypher, IEmbeddingService embeddings, ILogger<Neo4jHeadVectorStore> logger)
    {
        _cypher = cypher;
        _embeddings = embeddings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnsureIndexAsync(CancellationToken ct)
    {
        var dimensions = _embeddings.Dimensions;
        if (dimensions <= 0)
            return;

        try
        {
            // Index OPTIONS cannot be parameterized; the dimension is an internal int.
            await _cypher.ExecuteAsync(
                "CREATE VECTOR INDEX " + IndexName + " IF NOT EXISTS "
                + "FOR (h:HeadVector) ON (h.embedding) "
                + "OPTIONS {indexConfig: {`vector.dimensions`: " + dimensions
                + ", `vector.similarity_function`: 'cosine'}}",
                ct: ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Vector index '{Index}' ensured (dimensions={Dim}) | {Component}", IndexName, dimensions, "AKG");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not ensure head-vector index (provider may lack vector indexes); "
                + "stage-1 search will use app-side cosine | {Component}", "AKG");
        }
    }

    /// <inheritdoc />
    public async Task RebuildAsync(CancellationToken ct)
    {
        if (!_embeddings.IsAvailable)
        {
            _logger.LogInformation("Embedding service unavailable; skipping head-vector rebuild | {Component}", "AKG");
            return;
        }

        await EnsureIndexAsync(ct).ConfigureAwait(false);

        // Repository / upload heads are exactly the two-segment ids under the git:/upload: prefixes
        // (e.g. git:<repo>, upload:<source>); their file leaves carry a third segment. Only (re)build heads
        // that are flagged dirty (a descendant file was re-embedded) or have no centroids yet (first run /
        // new repos), so unchanged repositories are not re-clustered on every backfill cycle.
        var heads = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE (r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:') AND size(split(r.id, ':')) = 2
            OPTIONAL MATCH (hv:HeadVector {headId: r.id})
            WITH r, count(hv) AS vectors
            WHERE coalesce(r.headVectorDirty, false) = true OR vectors = 0
            RETURN r.id AS id, r.ownerId AS ownerId
            """,
            ct: ct).ConfigureAwait(false);

        var rebuilt = 0;
        foreach (var head in heads)
        {
            ct.ThrowIfCancellationRequested();

            var headId = head.TryGetValue("id", out var idValue) ? idValue?.ToString() : null;
            if (string.IsNullOrEmpty(headId))
                continue;
            var ownerId = head.TryGetValue("ownerId", out var ownerValue) ? ownerValue?.ToString() : null;

            var embeddings = await FetchSubtreeEmbeddingsAsync(headId, ct).ConfigureAwait(false);
            if (embeddings.Count == 0)
                continue;

            var k = Math.Clamp(embeddings.Count / ChunksPerCentroid, 1, MaxCentroids);
            var centroids = KMeans.Cluster(embeddings, k);
            if (centroids.Count == 0)
                continue;

            await PersistHeadAsync(headId, ownerId, centroids, ct).ConfigureAwait(false);
            await ClearHeadDirtyAsync(headId, ct).ConfigureAwait(false);
            rebuilt++;
        }

        _logger.LogInformation("Head vectors rebuilt for {Count} head(s) | {Component}", rebuilt, "AKG");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HeadMatch>> FindTopHeadsAsync(
        float[] queryEmbedding, int topK, double threshold, string? userId, CancellationToken ct)
    {
        if (queryEmbedding.Length == 0 || topK <= 0)
            return [];

        try
        {
            // Fetch more centroids than heads (a head has several) so enough distinct heads survive the
            // per-head max-aggregation below.
            var rows = await _cypher.QueryAsync(
                """
                CALL db.index.vector.queryNodes($index, $topK, $vector)
                YIELD node, score
                WHERE score > $threshold AND (node.ownerId IS NULL OR node.ownerId = $userId)
                RETURN node.headId AS id, max(score) AS score
                """,
                new
                {
                    index = IndexName,
                    topK = topK * MaxCentroids,
                    vector = queryEmbedding,
                    threshold,
                    userId,
                },
                ct).ConfigureAwait(false);

            return RankHeads(rows.Select(r => (
                Id: r.TryGetValue("id", out var i) ? i?.ToString() : null,
                Score: r.TryGetValue("score", out var s) && s is not null ? Convert.ToDouble(s) : 0.0)), topK);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Head-vector index '{Index}' unavailable; using app-side cosine fallback | {Component}",
                IndexName, "AKG");
            return await FindTopHeadsAppSideAsync(queryEmbedding, topK, threshold, userId, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<(int HeadsWithVectors, int TotalHeads)> GetCoverageAsync(CancellationToken ct)
    {
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE (r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:') AND size(split(r.id, ':')) = 2
            OPTIONAL MATCH (hv:HeadVector {headId: r.id})
            WITH r, count(hv) AS vectors
            RETURN count(r) AS totalHeads, count(CASE WHEN vectors > 0 THEN 1 END) AS withVectors
            """,
            ct: ct).ConfigureAwait(false);

        if (rows.Count == 0)
            return (0, 0);

        var row = rows[0];
        int Get(string key) => row.TryGetValue(key, out var v) && v is not null ? Convert.ToInt32(v) : 0;
        return (Get("withVectors"), Get("totalHeads"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<float[]>> FetchSubtreeEmbeddingsAsync(string headId, CancellationToken ct)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (c:RuleChunk) WHERE c.parentId STARTS WITH $prefix RETURN c.embedding AS emb",
            new { prefix = headId + ":" },
            ct).ConfigureAwait(false);

        var embeddings = new List<float[]>(rows.Count);
        foreach (var row in rows)
        {
            var vector = ToFloatArray(row.TryGetValue("emb", out var e) ? e : null);
            if (vector.Length > 0)
                embeddings.Add(vector);
        }

        return embeddings;
    }

    private async Task PersistHeadAsync(
        string headId, string? ownerId, IReadOnlyList<float[]> centroids, CancellationToken ct)
    {
        // Replace the head's centroid set atomically: drop the old vectors, then recreate from scratch.
        await _cypher.ExecuteAsync(
            "MATCH (h:HeadVector {headId: $id}) DETACH DELETE h",
            new { id = headId },
            ct).ConfigureAwait(false);

        var maps = new List<Dictionary<string, object?>>(centroids.Count);
        for (var i = 0; i < centroids.Count; i++)
        {
            maps.Add(new Dictionary<string, object?>
            {
                ["ord"] = i,
                ["emb"] = centroids[i].Cast<object>().ToList(),
            });
        }

        await _cypher.ExecuteAsync(
            """
            UNWIND $vectors AS v
            CREATE (:HeadVector { headId: $id, ord: v.ord, embedding: v.emb, ownerId: $ownerId })
            """,
            new { id = headId, ownerId, vectors = maps },
            ct).ConfigureAwait(false);
    }

    private async Task ClearHeadDirtyAsync(string headId, CancellationToken ct)
        => await _cypher.ExecuteAsync(
            "MATCH (h:Rule {id: $id}) SET h.headVectorDirty = false",
            new { id = headId },
            ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<HeadMatch>> FindTopHeadsAppSideAsync(
        float[] queryEmbedding, int topK, double threshold, string? userId, CancellationToken ct)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows;
        try
        {
            rows = await _cypher.QueryAsync(
                """
                MATCH (h:HeadVector)
                WHERE h.ownerId IS NULL OR h.ownerId = $userId
                RETURN h.headId AS id, h.embedding AS emb
                """,
                new { userId },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load head vectors for app-side cosine | {Component}", "AKG");
            return [];
        }

        var scored = rows.Select(r => (
            Id: r.TryGetValue("id", out var i) ? i?.ToString() : null,
            Score: (double)CosineSimilarity(queryEmbedding, ToFloatArray(r.TryGetValue("emb", out var e) ? e : null))))
            .Where(x => x.Score > threshold);

        return RankHeads(scored, topK);
    }

    /// <summary>Collapses per-centroid scores to the best score per head, then takes the top heads.</summary>
    private static IReadOnlyList<HeadMatch> RankHeads(IEnumerable<(string? Id, double Score)> scored, int topK)
    {
        var best = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, score) in scored)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            best[id] = best.TryGetValue(id, out var existing) ? Math.Max(existing, score) : score;
        }

        return best
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select(kv => new HeadMatch(kv.Key, kv.Value))
            .ToList();
    }

    private static float[] ToFloatArray(object? value)
        => value is IEnumerable<object> list ? list.Select(Convert.ToSingle).ToArray() : [];

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
