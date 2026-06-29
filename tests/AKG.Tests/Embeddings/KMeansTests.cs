using Edda.AKG.Embeddings;

namespace Edda.AKG.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="KMeans"/>: deterministic clustering of embedding vectors into normalized
/// centroids, including the single-cluster and clamp-to-vector-count edge cases.
/// </summary>
public class KMeansTests
{
    private static double Norm(float[] v) => Math.Sqrt(v.Sum(x => (double)x * x));

    [Fact]
    public void Cluster_EmptyInput_ReturnsEmpty()
        => KMeans.Cluster([], 3).Should().BeEmpty();

    [Fact]
    public void Cluster_SingleCluster_ReturnsOneNormalizedCentroid()
    {
        var vectors = new[] { new[] { 2f, 0f, 0f }, new[] { 4f, 0f, 0f } };

        var centroids = KMeans.Cluster(vectors, 1);

        centroids.Should().ContainSingle();
        Norm(centroids[0]).Should().BeApproximately(1.0, 1e-5);
        centroids[0][0].Should().BeApproximately(1f, 1e-5f); // mean (3,0,0) → normalized (1,0,0)
    }

    [Fact]
    public void Cluster_SeparableGroups_ReturnsOneCentroidPerGroup()
    {
        var vectors = new[]
        {
            new[] { 1f, 0f, 0f }, new[] { 0.9f, 0.1f, 0f },   // group along x
            new[] { 0f, 0f, 1f }, new[] { 0f, 0.1f, 0.9f },   // group along z
        };

        var centroids = KMeans.Cluster(vectors, 2);

        centroids.Should().HaveCount(2);
        var dominantDims = centroids.Select(c => Array.IndexOf(c, c.Max())).ToList();
        dominantDims.Should().Contain(0);
        dominantDims.Should().Contain(2);
        centroids.Should().OnlyContain(c => Math.Abs(Norm(c) - 1.0) < 1e-5);
    }

    [Fact]
    public void Cluster_MoreClustersThanVectors_ClampsToVectorCount()
    {
        var vectors = new[] { new[] { 1f, 0f }, new[] { 0f, 1f } };

        KMeans.Cluster(vectors, 5).Count.Should().BeLessThanOrEqualTo(2);
    }
}
