using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Tests.Graph;

/// <summary>Unit tests for <see cref="CypherGraphStore"/> (ICypherExecutor + ambient identity mocked).</summary>
public class CypherGraphStoreTests
{
    private readonly Mock<ICypherExecutor> _cypher = new();
    private readonly Mock<IIdentityContext> _identity = new();
    private readonly CypherGraphStore _sut;

    public CypherGraphStoreTests()
    {
        _identity.SetupGet(i => i.TenantId).Returns("default");
        _sut = new CypherGraphStore(_cypher.Object, _identity.Object);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> NodeRows(string column, params string[] ids)
        => ids.Select(id => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            [column] = new Dictionary<string, object?>
            {
                ["id"] = id, ["type"] = "Rule", ["domain"] = "general", ["priority"] = "Medium", ["body"] = "b",
            },
        }).ToList();

    private void SetupQuery(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => _cypher.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(rows);

    private object? CaptureParams(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        object? captured = null;
        _cypher.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
               .Callback<string, object?, CancellationToken>((_, p, _) => captured = p)
               .ReturnsAsync(rows);
        return captured; // note: filled by the callback when the query runs
    }

    [Fact]
    public async Task GetRuleAsync_Found_MapsRule()
    {
        SetupQuery(NodeRows("r", "rule-1"));

        var rule = await _sut.GetRuleAsync("rule-1");

        rule.Should().NotBeNull();
        rule!.Id.Should().Be("rule-1");
    }

    [Fact]
    public async Task GetRuleAsync_NotFound_ReturnsNull()
    {
        SetupQuery([]);

        (await _sut.GetRuleAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetRulesAsync_MapsAll()
    {
        SetupQuery(NodeRows("r", "a", "b"));

        (await _sut.GetRulesAsync()).Select(r => r.Id).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task GetRuleHeadsAsync_MapsHeads()
    {
        SetupQuery(NodeRows("r", "h1"));

        (await _sut.GetRuleHeadsAsync()).Select(r => r.Id).Should().BeEquivalentTo("h1");
    }

    [Fact]
    public async Task FindNeighborsAsync_MapsNeighbors()
    {
        SetupQuery(NodeRows("n", "n1"));

        (await _sut.FindNeighborsAsync("r0")).Select(r => r.Id).Should().BeEquivalentTo("n1");
    }

    [Fact]
    public async Task ListOwnersAsync_ReturnsDistinctOwners()
    {
        SetupQuery(
        [
            new Dictionary<string, object?> { ["ownerId"] = "alice" },
            new Dictionary<string, object?> { ["ownerId"] = "bob" },
        ]);

        (await _sut.ListOwnersAsync("Memory")).Should().BeEquivalentTo("alice", "bob");
    }

    [Fact]
    public async Task GetRuleAsync_PassesAmbientTenantToQuery()
    {
        _identity.SetupGet(i => i.TenantId).Returns("acme");
        object? captured = null;
        _cypher.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
               .Callback<string, object?, CancellationToken>((_, p, _) => captured = p)
               .ReturnsAsync([]);

        await _sut.GetRuleAsync("x");

        captured!.GetType().GetProperty("tenantId")!.GetValue(captured).Should().Be("acme");
    }

    [Fact]
    public async Task NullIdentity_UsesDefaultTenant()
    {
        var sut = new CypherGraphStore(_cypher.Object, identity: null);
        object? captured = null;
        _cypher.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
               .Callback<string, object?, CancellationToken>((_, p, _) => captured = p)
               .ReturnsAsync([]);

        await sut.GetRuleAsync("x");

        captured!.GetType().GetProperty("tenantId")!.GetValue(captured).Should().Be("default");
    }

    // ---- Write operations (Slice 1b) ----

    private static KnowledgeRule Rule(string id = "r1")
        => new() { Id = id, Type = "Memory", Domain = "general", Priority = RulePriority.Medium, Body = "b" };

    private void SetupExecute()
        => _cypher.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

    [Fact]
    public async Task UpsertRuleGraphAsync_WritesNodeAndSixEdges()
    {
        SetupExecute();

        await _sut.UpsertRuleGraphAsync(Rule());

        // One node MERGE plus six typed-edge writes = seven executes.
        _cypher.Verify(c => c.ExecuteAsync(It.Is<string>(q => q.Contains("MERGE (r:Rule {id: $id})")),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        _cypher.Verify(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(7));
    }

    [Fact]
    public async Task UpsertRuleGraphAsync_StampsAmbientTenant()
    {
        _identity.SetupGet(i => i.TenantId).Returns("acme");
        object? captured = null;
        _cypher.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
               .Callback<string, object?, CancellationToken>((q, p, _) =>
               {
                   if (q.Contains("MERGE (r:Rule {id: $id})")) captured = p;
               })
               .Returns(Task.CompletedTask);

        await _sut.UpsertRuleGraphAsync(Rule());

        captured!.GetType().GetProperty("tenantId")!.GetValue(captured).Should().Be("acme");
    }

    [Fact]
    public async Task DeleteRuleGraphAsync_SoftDeletesWithActingUser()
    {
        object? captured = null;
        _cypher.Setup(c => c.ExecuteAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
               .Callback<string, object?, CancellationToken>((_, p, _) => captured = p)
               .Returns(Task.CompletedTask);

        await _sut.DeleteRuleGraphAsync("r1", "alice");

        _cypher.Verify(c => c.ExecuteAsync(It.Is<string>(q => q.Contains("r.deletedAt = $now")),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        captured!.GetType().GetProperty("userId")!.GetValue(captured).Should().Be("alice");
    }

    [Fact]
    public async Task DeleteSubtreeGraphAsync_ReturnsCountAndHardDeletes()
    {
        _cypher.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([new Dictionary<string, object?> { ["n"] = 3 }]);
        SetupExecute();

        var deleted = await _sut.DeleteSubtreeGraphAsync("root", new[] { "root:" });

        deleted.Should().Be(3);
        _cypher.Verify(c => c.ExecuteAsync(It.Is<string>(q => q.Contains("DETACH DELETE")),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateSupersededAsync_ClosesSupersededValidity()
    {
        SetupExecute();

        await _sut.InvalidateSupersededAsync();

        _cypher.Verify(c => c.ExecuteAsync(It.Is<string>(q => q.Contains("invalidatedBy = newer.id")),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
