using Edda.Core.Abstractions;

namespace Edda.AKG.Context;

/// <summary>
/// Cypher/Neo4j-backed <see cref="IVectorStore"/> (ADR-0013): implements vector search via the native Neo4j
/// vector index (<c>db.index.vector.queryNodes</c> over <c>(:RuleChunk).embedding</c>) and reads stored chunk
/// embeddings, both mapped back to the parent rule. <see cref="SearchByVectorAsync"/> throws when the index is
/// unavailable (missing, or unsupported by the provider — e.g. Memgraph), letting the caller fall back to
/// app-side cosine scoring. Works over any Cypher backend and, unchanged, over the in-memory dev executor.
/// </summary>
internal sealed class CypherVectorStore : IVectorStore
{
    /// <summary>Name of the Neo4j vector index over <c>(:RuleChunk).embedding</c>.</summary>
    private const string VectorIndexName = "chunk_embeddings";

    private readonly ICypherExecutor _cypher;

    /// <summary>Initializes a new instance of the <see cref="CypherVectorStore"/> class.</summary>
    /// <param name="cypher">Executor for the vector-index query and embedding retrieval.</param>
    public CypherVectorStore(ICypherExecutor cypher) => _cypher = cypher;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, double>> SearchByVectorAsync(
        float[] queryVector,
        int topK,
        double threshold,
        string? userId,
        CancellationToken cancellationToken = default)
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
                topK,
                vector = queryVector,
                threshold,
                userId,
            },
            cancellationToken).ConfigureAwait(false);

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = row.TryGetValue("id", out var i) ? i?.ToString() : null;
            if (id is null) continue;
            if (row.TryGetValue("score", out var sc) && sc is not null)
            {
                var value = Convert.ToDouble(sc);
                scores[id] = scores.TryGetValue(id, out var existing) ? Math.Max(existing, value) : value;
            }
        }

        return scores;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, float[]>> GetRepresentativeEmbeddingsAsync(
        IReadOnlyList<string> ruleIds,
        CancellationToken cancellationToken = default)
    {
        if (ruleIds.Count == 0)
            return new Dictionary<string, float[]>();

        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)-[:HAS_CHUNK]->(c:RuleChunk)
            WHERE r.id IN $ids
            WITH r, c ORDER BY c.ord
            RETURN r.id AS id, collect(c.embedding)[0] AS emb
            """,
            new { ids = ruleIds },
            cancellationToken).ConfigureAwait(false);

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

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<float[]>>> GetChunkEmbeddingsAsync(
        IReadOnlyList<string> ruleIds,
        CancellationToken cancellationToken = default)
    {
        if (ruleIds.Count == 0)
            return new Dictionary<string, IReadOnlyList<float[]>>();

        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)-[:HAS_CHUNK]->(c:RuleChunk)
            WHERE r.id IN $ids
            RETURN r.id AS id, collect(c.embedding) AS embs
            """,
            new { ids = ruleIds },
            cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<string, IReadOnlyList<float[]>>(StringComparer.Ordinal);
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
}
