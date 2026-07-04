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

    /// <summary>Default number of co-occurrence query-expansion terms (B5). 0 = expansion off.</summary>
    public const int DefaultQueryExpansionTerms = 0;

    /// <summary>Default score weight of an expanded-term match relative to a direct match (B5).</summary>
    public const double DefaultQueryExpansionWeight = 0.5;

    /// <summary>Default RRF dampening constant (standard value 60): higher flattens each rank's contribution.</summary>
    public const int DefaultRrfK = 60;

    /// <summary>Default RRF weight of the keyword ranking (1.0 = neutral, equal to the semantic weight).</summary>
    public const double DefaultRrfKeywordWeight = 1.0;

    /// <summary>Default RRF weight of the semantic ranking (1.0 = neutral, equal to the keyword weight).</summary>
    public const double DefaultRrfSemanticWeight = 1.0;

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

    /// <summary>
    /// Number of related terms added to the keyword path via concept co-occurrence over the curated
    /// knowledge (B5). 0 (the default) disables query expansion — behavior is then unchanged.
    /// </summary>
    public int QueryExpansionTerms { get; init; } = DefaultQueryExpansionTerms;

    /// <summary>Score weight of an expanded-term match relative to a direct match (B5).</summary>
    public double QueryExpansionWeight { get; init; } = DefaultQueryExpansionWeight;

    /// <summary>Reciprocal Rank Fusion dampening constant — higher flattens the per-rank contribution.</summary>
    public int RrfK { get; init; } = DefaultRrfK;

    /// <summary>RRF weight applied to the keyword ranking's contribution (1.0 = neutral).</summary>
    public double RrfKeywordWeight { get; init; } = DefaultRrfKeywordWeight;

    /// <summary>RRF weight applied to the semantic ranking's contribution (1.0 = neutral).</summary>
    public double RrfSemanticWeight { get; init; } = DefaultRrfSemanticWeight;
}
