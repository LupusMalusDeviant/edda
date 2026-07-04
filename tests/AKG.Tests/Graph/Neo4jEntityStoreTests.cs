using Edda.AKG.Graph;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Graph;

public class Neo4jEntityStoreTests
{
    private readonly FakeCypherExecutor _cypher = new();
    private readonly Neo4jEntityStore _sut;

    public Neo4jEntityStoreTests()
    {
        _sut = new Neo4jEntityStore(_cypher, TimeProvider.System, NullLogger<Neo4jEntityStore>.Instance);
    }

    [Fact]
    public async Task IngestAsync_MergesEntitiesAndRelations()
    {
        var extraction = new EntityExtractionResult
        {
            Entities =
            [
                new ExtractedEntity { Name = "Neo4j", Type = "technology", Description = "graph db" },
                new ExtractedEntity { Name = "Cypher", Type = "technology", Description = "query lang" },
            ],
            Relations =
            [
                new ExtractedRelation { Source = "Cypher", Target = "Neo4j", Description = "queries" },
            ],
        };

        var result = await _sut.IngestAsync(extraction, "user-1", "chat");

        result.EntitiesIngested.Should().Be(2);
        result.RelationsIngested.Should().Be(1);
        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("MERGE (e:Entity"));
        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("RELATES_TO"));
    }

    [Fact]
    public async Task IngestAsync_EmptyExtraction_NoWrites()
    {
        var result = await _sut.IngestAsync(EntityExtractionResult.Empty, "user-1", "chat");

        result.Should().BeSameAs(EntityIngestionResult.Empty);
        _cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_DuplicateNamesInBatch_DedupedToOne()
    {
        var extraction = new EntityExtractionResult
        {
            Entities =
            [
                new ExtractedEntity { Name = "Neo4j" },
                new ExtractedEntity { Name = "neo4j" }, // same normalized name
            ],
        };

        var result = await _sut.IngestAsync(extraction, "user-1", "chat");

        result.EntitiesIngested.Should().Be(1);
    }

    [Fact]
    public async Task IngestAsync_SelfRelation_Skipped()
    {
        var extraction = new EntityExtractionResult
        {
            Entities = [new ExtractedEntity { Name = "A" }],
            Relations = [new ExtractedRelation { Source = "A", Target = "A" }],
        };

        var result = await _sut.IngestAsync(extraction, "user-1", "chat");

        result.RelationsIngested.Should().Be(0);
    }

    [Fact]
    public async Task FindEntitiesAsync_MapsRowsToGraphEntities()
    {
        _cypher.DefaultResult =
        [
            new Dictionary<string, object?>
            {
                ["name"] = "Neo4j",
                ["type"] = "technology",
                ["description"] = "graph db",
                ["mentions"] = 3L,
            },
        ];

        var result = await _sut.FindEntitiesAsync(["neo"], "user-1");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Neo4j");
        result[0].Type.Should().Be("technology");
        result[0].Mentions.Should().Be(3);
    }

    [Fact]
    public async Task FindEntitiesAsync_NoTerms_ReturnsEmptyWithoutQuery()
    {
        var result = await _sut.FindEntitiesAsync([], "user-1");

        result.Should().BeEmpty();
        _cypher.ExecutedQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelatedAsync_ScopesToUserInQuery()
    {
        _cypher.DefaultResult =
        [
            new Dictionary<string, object?>
            {
                ["name"] = "Cypher",
                ["type"] = "technology",
                ["description"] = "",
                ["mentions"] = 1L,
            },
        ];

        var result = await _sut.GetRelatedAsync("Neo4j", "user-1");

        result.Should().ContainSingle();
        result[0].Name.Should().Be("Cypher");
        _cypher.ExecutedQueries.Should().Contain(q =>
            q.Contains("RELATES_TO") && q.Contains("e.ownerId = $userId"));
    }

    private static IIdentityContext Identity(string tenant)
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.TenantId).Returns(tenant);
        return identity.Object;
    }

    [Fact]
    public async Task IngestAsync_StampsAmbientTenantOnEntityMerge()
    {
        var store = new Neo4jEntityStore(
            _cypher, TimeProvider.System, NullLogger<Neo4jEntityStore>.Instance, Identity("acme"));

        await store.IngestAsync(
            new EntityExtractionResult { Entities = [new ExtractedEntity { Name = "Neo4j" }] }, "user-1", "chat");

        var mergeIndex = _cypher.ExecutedWriteQueries.FindIndex(q => q.Contains("MERGE (e:Entity"));
        mergeIndex.Should().BeGreaterThanOrEqualTo(0);
        _cypher.ExecutedWriteQueries[mergeIndex].Should().Contain("tenantId: $tenantId");
        var mergeParams = _cypher.ExecutedWriteParameters[mergeIndex]!;
        mergeParams.GetType().GetProperty("tenantId")!.GetValue(mergeParams).Should().Be("acme");
    }

    [Fact]
    public async Task IngestAsync_NoIdentity_StampsDefaultTenant()
    {
        await _sut.IngestAsync(
            new EntityExtractionResult { Entities = [new ExtractedEntity { Name = "Neo4j" }] }, "user-1", "chat");

        var mergeIndex = _cypher.ExecutedWriteQueries.FindIndex(q => q.Contains("MERGE (e:Entity"));
        var mergeParams = _cypher.ExecutedWriteParameters[mergeIndex]!;
        mergeParams.GetType().GetProperty("tenantId")!.GetValue(mergeParams).Should().Be("default");
    }

    [Fact]
    public async Task FindEntitiesAsync_FiltersByTenant()
    {
        await _sut.FindEntitiesAsync(["neo"], "user-1");

        _cypher.ExecutedQueries.Should().Contain(q => q.Contains("coalesce(e.tenantId, 'default') = $tenantId"));
    }

    [Fact]
    public async Task GetRelatedAsync_FiltersByTenant()
    {
        await _sut.GetRelatedAsync("Neo4j", "user-1");

        _cypher.ExecutedQueries.Should().Contain(q => q.Contains("coalesce(e.tenantId, 'default') = $tenantId"));
    }
}
