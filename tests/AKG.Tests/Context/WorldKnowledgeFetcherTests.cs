using Edda.AKG.Context;
using Edda.AKG.Tests.TestUtilities;

namespace Edda.AKG.Tests.Context;

public sealed class WorldKnowledgeFetcherTests
{
    private static IReadOnlyDictionary<string, object?> MakeFakeWorldNode(string id, string domain, string[] tags) =>
        new Dictionary<string, object?>
        {
            ["id"]       = id,
            ["type"]     = "WorldKnowledge",
            ["domain"]   = domain,
            ["priority"] = "Low",
            ["body"]     = $"Body of {id}",
            ["tags"]     = (object)tags,
        };

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> WrapNode(
        IReadOnlyDictionary<string, object?> node)
        => [new Dictionary<string, object?> { ["w"] = node }];

    [Fact]
    public async Task FetchAsync_EmptyConcepts_ReturnsEmptyWithoutQuerying()
    {
        var cypher = new FakeCypherExecutor();
        var sut = new WorldKnowledgeFetcher(cypher);

        var result = await sut.FetchAsync([], CancellationToken.None);

        result.Should().BeEmpty();
        cypher.ExecutedQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_MatchingConcept_ReturnsRules()
    {
        var cypher = new FakeCypherExecutor();
        var node = MakeFakeWorldNode("world-oop", "world", ["oop", "solid"]);
        cypher.DefaultResult = WrapNode(node);
        var sut = new WorldKnowledgeFetcher(cypher);

        var result = await sut.FetchAsync(["oop"], CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("world-oop");
        result[0].Type.Should().Be("WorldKnowledge");
    }

    [Fact]
    public async Task FetchAsync_NoMatchingNodes_ReturnsEmpty()
    {
        var cypher = new FakeCypherExecutor();
        // DefaultResult is already empty
        var sut = new WorldKnowledgeFetcher(cypher);

        var result = await sut.FetchAsync(["some-concept"], CancellationToken.None);

        result.Should().BeEmpty();
        cypher.ExecutedQueries.Should().ContainSingle(q => q.Contains("WorldKnowledge"));
    }

    [Fact]
    public async Task FetchAsync_MultipleConcepts_PassesConceptsToQuery()
    {
        var cypher = new FakeCypherExecutor();
        cypher.DefaultResult = [];
        var sut = new WorldKnowledgeFetcher(cypher);

        await sut.FetchAsync(["security", "authentication", "zero-trust"], CancellationToken.None);

        cypher.ExecutedQueries.Should().ContainSingle(q =>
            q.Contains("WorldKnowledge") && q.Contains("$concepts"));
    }
}
