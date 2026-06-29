using Edda.AKG.Context;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Context;

/// <summary>
/// Unit tests for <see cref="RuleMmrReranker"/>: MMR diversification prefers a diverse rule over a
/// near-duplicate of an already-selected one, and degrades to the input order when there are too few
/// embedded candidates.
/// </summary>
public class RuleMmrRerankerTests
{
    private static ScoredRule Scored(string id, double score)
        => new()
        {
            Rule = new KnowledgeRule
            {
                Id = id, Type = "Rule", Domain = "general",
                Priority = RulePriority.Medium, Body = "b",
            },
            Score = score,
        };

    [Fact]
    public void Rerank_PrefersDiverseOverNearDuplicate()
    {
        var ranked = new[] { Scored("r1", 1.0), Scored("r2", 0.6), Scored("r3", 0.55) };
        var embeddings = new Dictionary<string, float[]>(StringComparer.Ordinal)
        {
            ["r1"] = [1f, 0f, 0f],
            ["r2"] = [1f, 0f, 0f],   // near-duplicate of r1
            ["r3"] = [0f, 1f, 0f],   // diverse
        };

        var result = RuleMmrReranker.Rerank(ranked, embeddings, k: 2, lambda: 0.7);

        // r1 (top relevance) first, then the diverse r3 ahead of the near-duplicate r2.
        result.Select(r => r.Rule.Id).Should().Equal("r1", "r3", "r2");
    }

    [Fact]
    public void Rerank_NoEmbeddings_ReturnsUnchanged()
    {
        var ranked = new[] { Scored("a", 1.0), Scored("b", 0.5) };

        var result = RuleMmrReranker.Rerank(ranked, new Dictionary<string, float[]>(), k: 2);

        result.Select(r => r.Rule.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void Rerank_SingleEmbeddedCandidate_ReturnsUnchanged()
    {
        var ranked = new[] { Scored("a", 1.0), Scored("b", 0.5) };
        var embeddings = new Dictionary<string, float[]>(StringComparer.Ordinal) { ["a"] = [1f, 0f] };

        var result = RuleMmrReranker.Rerank(ranked, embeddings, k: 2);

        result.Select(r => r.Rule.Id).Should().Equal("a", "b");
    }
}
