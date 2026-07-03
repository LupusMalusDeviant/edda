using Edda.AKG.Context;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Models;
using Moq;
using Neo4j.Driver;

namespace Edda.AKG.Tests.Context;

/// <summary>
/// Unit tests for <see cref="GraphExpander"/>: multi-hop breadth-first expansion, depth limiting,
/// deduplication, and the seed-only fallback. A stateful query handler returns a different neighbour
/// set per BFS level (the per-level Cypher text is identical; only the <c>$frontier</c> param differs).
/// </summary>
public class GraphExpanderTests
{
    private static INode RuleNode(string id)
    {
        var node = new Mock<INode>();
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "Rule",
            ["domain"] = "general",
            ["priority"] = "Medium",
            ["body"] = "body",
            ["tags"] = new List<object>(),
        });
        return node.Object;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> NeighborRows(params string[] ids)
        => ids.Select(id => (IReadOnlyDictionary<string, object?>)
                new Dictionary<string, object?> { ["n"] = RuleNode(id) })
              .ToList();

    private static KnowledgeRule Seed(string id)
        => new() { Id = id, Type = "Rule", Domain = "general", Priority = RulePriority.Medium, Body = "b" };

    private static GraphExpander ExpanderReturning(
        params IReadOnlyList<IReadOnlyDictionary<string, object?>>[] perLevel)
    {
        var responses = new Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(perLevel);
        var cypher = new FakeCypherExecutor();
        cypher.AddQueryHandler(_ => responses.Count > 0 ? responses.Dequeue() : cypher.DefaultResult);
        return new GraphExpander(cypher, TimeProvider.System);
    }

    [Fact]
    public async Task ExpandAsync_MultiHop_ReachesSecondHop()
    {
        // depth 0: A → [B]; depth 1: B → [C]
        var sut = ExpanderReturning(NeighborRows("B"), NeighborRows("C"));

        var result = await sut.ExpandAsync([Seed("A")], userId: null, CancellationToken.None, maxDepth: 2);

        result.Select(r => r.Id).Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    public async Task ExpandAsync_DepthOne_StopsAtFirstHop()
    {
        var sut = ExpanderReturning(NeighborRows("B"), NeighborRows("C"));

        var result = await sut.ExpandAsync([Seed("A")], userId: null, CancellationToken.None, maxDepth: 1);

        result.Select(r => r.Id).Should().BeEquivalentTo("A", "B");
        result.Select(r => r.Id).Should().NotContain("C");
    }

    [Fact]
    public async Task ExpandAsync_DeduplicatesAndExcludesSeeds()
    {
        // First hop repeats the seed (A) and a duplicate (B).
        var sut = ExpanderReturning(NeighborRows("B", "A", "B"));

        var result = await sut.ExpandAsync([Seed("A")], userId: null, CancellationToken.None, maxDepth: 1);

        result.Select(r => r.Id).Should().Equal("A", "B");
    }

    [Fact]
    public async Task ExpandAsync_NoNeighbors_ReturnsSeedsOnly()
    {
        var sut = ExpanderReturning();   // queue empty → default empty result

        var result = await sut.ExpandAsync([Seed("A"), Seed("B")], userId: null, CancellationToken.None);

        result.Select(r => r.Id).Should().Equal("A", "B");
    }
}
