namespace Edda.Core.Models;

/// <summary>
/// Tunable thresholds and top-K limits for the hybrid retrieval pipeline (semantic boosting, MMR
/// reranking, and hierarchical head pre-pruning). Bound from the <c>RETRIEVAL_*</c> configuration keys;
/// every default matches the historical hard-coded value, so the resolved options change nothing unless
/// the operator overrides them.
/// </summary>
public sealed record RetrievalOptions
{
    /// <summary>Default minimum cosine similarity for a rule to count as a semantic match.</summary>
    public const double DefaultSimilarityThreshold = 0.5;

    /// <summary>Default number of nearest chunk neighbours requested from the vector index.</summary>
    public const int DefaultVectorTopK = 100;

    /// <summary>Default number of top candidates diversified with MMR.</summary>
    public const int DefaultMmrTopN = 15;

    /// <summary>Default MMR relevance/diversity trade-off (1.0 = pure relevance, 0.0 = pure diversity).</summary>
    public const double DefaultMmrLambda = 0.7;

    /// <summary>Default minimum head-centroid cosine similarity for a head to qualify in stage 1.</summary>
    public const double DefaultHeadSimilarityThreshold = 0.4;

    /// <summary>
    /// Default cap on the number of keyword-ranked candidate rules the app-side cosine fallback scores
    /// (used only when the native vector index is unavailable, e.g. on Memgraph). Bounds the O(N) fallback.
    /// </summary>
    public const int DefaultFallbackMaxCandidates = 500;

    /// <summary>Minimum cosine similarity for a rule to be considered a semantic match.</summary>
    public double SimilarityThreshold { get; init; } = DefaultSimilarityThreshold;

    /// <summary>Number of nearest chunk neighbours requested from the vector index.</summary>
    public int VectorTopK { get; init; } = DefaultVectorTopK;

    /// <summary>Number of top candidates to diversify with MMR.</summary>
    public int MmrTopN { get; init; } = DefaultMmrTopN;

    /// <summary>MMR relevance/diversity trade-off (1.0 = pure relevance, 0.0 = pure diversity).</summary>
    public double MmrLambda { get; init; } = DefaultMmrLambda;

    /// <summary>Minimum head-centroid cosine similarity for a head to qualify in stage-1 pre-pruning.</summary>
    public double HeadSimilarityThreshold { get; init; } = DefaultHeadSimilarityThreshold;

    /// <summary>
    /// Maximum number of keyword-ranked candidate rules the app-side cosine fallback scores when the
    /// native vector index is unavailable. Above this the top-scoring candidates are kept and the rest
    /// dropped, bounding the fallback's O(N) cost. Ignored on the fast vector-index path.
    /// </summary>
    public int FallbackMaxCandidates { get; init; } = DefaultFallbackMaxCandidates;
}
