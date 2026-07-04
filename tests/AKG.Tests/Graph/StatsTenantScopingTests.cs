using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// C1 end-to-end: <see cref="CypherGraphStore.GetRuleStatisticsAsync"/> must count only the ambient tenant's
/// rules — proving the former cross-tenant stats leak (an unfiltered <c>MATCH (r:Rule)</c>) is closed across
/// both the Cypher parameter and the in-memory executor's tenant filter.
/// </summary>
public sealed class StatsTenantScopingTests
{
    private readonly ICypherExecutor _executor = new InMemoryCypherExecutor();

    private static IIdentityContext Identity(string tenant)
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.TenantId).Returns(tenant);
        return identity.Object;
    }

    private CypherGraphStore Store(string tenant) => new(_executor, Identity(tenant));

    private static KnowledgeRule Rule(string id, string domain = "general")
        => new() { Id = id, Type = "Rule", Domain = domain, Priority = RulePriority.Medium, Body = "b" };

    [Fact]
    public async Task GetRuleStatisticsAsync_CountsOnlyTheAmbientTenant()
    {
        await Store("tenant-a").UpsertRuleGraphAsync(Rule("a1"));
        await Store("tenant-a").UpsertRuleGraphAsync(Rule("a2"));
        await Store("tenant-b").UpsertRuleGraphAsync(Rule("b1"));

        (await Store("tenant-a").GetRuleStatisticsAsync()).TotalRules.Should().Be(2);
        (await Store("tenant-b").GetRuleStatisticsAsync()).TotalRules.Should().Be(1);
    }

    [Fact]
    public async Task GetRuleStatisticsAsync_DomainsAreTenantScoped()
    {
        await Store("tenant-a").UpsertRuleGraphAsync(Rule("a1", "security"));
        await Store("tenant-b").UpsertRuleGraphAsync(Rule("b1", "csharp"));

        var statsA = await Store("tenant-a").GetRuleStatisticsAsync();

        statsA.RulesByDomain.Should().ContainKey("security");
        statsA.RulesByDomain.Should().NotContainKey("csharp");
    }

    [Fact]
    public async Task GetRuleStatisticsAsync_DefaultTenant_CountsRuleIngestedWithoutAmbientTenant()
    {
        // Behaviour-neutral: no ambient identity → default tenant, and its rule is still counted.
        var store = new CypherGraphStore(_executor);
        await store.UpsertRuleGraphAsync(Rule("legacy"));

        (await store.GetRuleStatisticsAsync()).TotalRules.Should().Be(1);
    }
}
