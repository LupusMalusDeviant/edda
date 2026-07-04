using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// C1 end-to-end entity isolation: two entity stores with different ambient tenants share one in-memory
/// executor. Entities written under tenant A must be invisible under tenant B, and two tenants may hold
/// distinct entities that share the same (owner, name). Legacy entities without a tenant stay in the default.
/// </summary>
public sealed class EntityTenantIsolationTests
{
    private readonly ICypherExecutor _executor = new InMemoryCypherExecutor();

    private static IIdentityContext Identity(string tenant)
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.TenantId).Returns(tenant);
        return identity.Object;
    }

    private Neo4jEntityStore Store(string tenant)
        => new(_executor, TimeProvider.System, NullLogger<Neo4jEntityStore>.Instance, Identity(tenant));

    private static EntityExtractionResult OneEntity(string name, string description = "")
        => new() { Entities = [new ExtractedEntity { Name = name, Description = description }] };

    [Fact]
    public async Task Entity_IngestedUnderOneTenant_IsInvisibleToAnother()
    {
        await Store("tenant-a").IngestAsync(OneEntity("Neo4j"), "user-1", "chat");

        var fromA = await Store("tenant-a").FindEntitiesAsync(["neo4j"], "user-1");
        var fromB = await Store("tenant-b").FindEntitiesAsync(["neo4j"], "user-1");

        fromA.Select(e => e.Name).Should().ContainSingle().Which.Should().Be("Neo4j");
        fromB.Should().BeEmpty();
    }

    [Fact]
    public async Task Entity_SameOwnerAndName_IsSeparatePerTenant()
    {
        await Store("tenant-a").IngestAsync(OneEntity("Shared", "from-a"), "user-1", "chat");
        await Store("tenant-b").IngestAsync(OneEntity("Shared", "from-b"), "user-1", "chat");

        var a = await Store("tenant-a").FindEntitiesAsync(["shared"], "user-1");
        var b = await Store("tenant-b").FindEntitiesAsync(["shared"], "user-1");

        a.Should().ContainSingle().Which.Description.Should().Be("from-a");
        b.Should().ContainSingle().Which.Description.Should().Be("from-b");
    }

    [Fact]
    public async Task DefaultTenant_SeesEntityIngestedWithoutAmbientTenant()
    {
        var store = new Neo4jEntityStore(_executor, TimeProvider.System, NullLogger<Neo4jEntityStore>.Instance);
        await store.IngestAsync(OneEntity("Legacy"), "user-1", "chat");

        (await store.FindEntitiesAsync(["legacy"], "user-1")).Should().ContainSingle();
    }
}
