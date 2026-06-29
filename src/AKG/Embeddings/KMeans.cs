namespace Edda.AKG.Embeddings;

/// <summary>
/// Minimal deterministic k-means for embedding vectors (Lloyd's algorithm with farthest-first
/// initialization — no RNG, so results are reproducible and unit-testable). Used to derive several
/// centroids per head, so a thematically broad repository is represented by multiple vectors instead of one
/// blurry average (see ADR-0009).
/// </summary>
internal static class KMeans
{
    /// <summary>
    /// Clusters <paramref name="vectors"/> into at most <paramref name="k"/> groups and returns the
    /// L2-normalized centroid of each non-empty cluster. Falls back to a single centroid when k &lt;= 1 or
    /// there are no more vectors than clusters.
    /// </summary>
    /// <param name="vectors">Input vectors; all are expected to share the same dimension.</param>
    /// <param name="k">Desired cluster count (clamped to [1, vectors.Count]).</param>
    /// <param name="maxIterations">Maximum Lloyd iterations.</param>
    /// <returns>L2-normalized centroids, one per non-empty cluster.</returns>
    public static IReadOnlyList<float[]> Cluster(
        IReadOnlyList<float[]> vectors, int k, int maxIterations = 10)
    {
        if (vectors.Count == 0)
            return [];

        var dim = vectors[0].Length;
        k = Math.Clamp(k, 1, vectors.Count);
        if (k == 1)
            return [Normalize(Mean(vectors, dim))];

        var centroids = FarthestFirstInit(vectors, k);
        var assignments = new int[vectors.Count];

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var changed = false;
            for (var i = 0; i < vectors.Count; i++)
            {
                var nearest = NearestCentroid(vectors[i], centroids);
                if (nearest != assignments[i])
                {
                    assignments[i] = nearest;
                    changed = true;
                }
            }

            if (iteration > 0 && !changed)
                break;

            for (var c = 0; c < k; c++)
            {
                var members = new List<float[]>();
                for (var i = 0; i < vectors.Count; i++)
                    if (assignments[i] == c)
                        members.Add(vectors[i]);
                if (members.Count > 0)
                    centroids[c] = Mean(members, dim);
            }
        }

        var result = new List<float[]>(k);
        for (var c = 0; c < k; c++)
        {
            var hasMembers = false;
            for (var i = 0; i < vectors.Count; i++)
            {
                if (assignments[i] == c)
                {
                    hasMembers = true;
                    break;
                }
            }

            if (hasMembers)
                result.Add(Normalize(centroids[c]));
        }

        return result;
    }

    /// <summary>Picks <paramref name="k"/> well-spread seeds deterministically (first vector, then farthest).</summary>
    private static float[][] FarthestFirstInit(IReadOnlyList<float[]> vectors, int k)
    {
        var centroids = new float[k][];
        centroids[0] = (float[])vectors[0].Clone();

        for (var c = 1; c < k; c++)
        {
            var farthestIndex = 0;
            var farthestDistance = -1.0;
            for (var i = 0; i < vectors.Count; i++)
            {
                var nearest = double.MaxValue;
                for (var j = 0; j < c; j++)
                    nearest = Math.Min(nearest, SquaredDistance(vectors[i], centroids[j]));
                if (nearest > farthestDistance)
                {
                    farthestDistance = nearest;
                    farthestIndex = i;
                }
            }

            centroids[c] = (float[])vectors[farthestIndex].Clone();
        }

        return centroids;
    }

    private static int NearestCentroid(float[] vector, float[][] centroids)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var c = 0; c < centroids.Length; c++)
        {
            var distance = SquaredDistance(vector, centroids[c]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = c;
            }
        }

        return best;
    }

    private static double SquaredDistance(float[] a, float[] b)
    {
        double sum = 0.0;
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            double diff = a[i] - b[i];
            sum += diff * diff;
        }

        return sum;
    }

    private static float[] Mean(IReadOnlyList<float[]> vectors, int dim)
    {
        var accumulator = new double[dim];
        foreach (var vector in vectors)
            for (var i = 0; i < dim && i < vector.Length; i++)
                accumulator[i] += vector[i];

        var mean = new float[dim];
        for (var i = 0; i < dim; i++)
            mean[i] = (float)(accumulator[i] / vectors.Count);

        return mean;
    }

    private static float[] Normalize(float[] vector)
    {
        double norm = 0.0;
        for (var i = 0; i < vector.Length; i++)
            norm += vector[i] * (double)vector[i];
        norm = Math.Sqrt(norm);
        if (norm == 0.0)
            return vector;

        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
            normalized[i] = (float)(vector[i] / norm);

        return normalized;
    }
}
