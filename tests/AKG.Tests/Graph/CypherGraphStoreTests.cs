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

    [Fact]
    public async Task GetCompilationRulesAsync_MapsScopedCandidates()
    {
        SetupQuery(NodeRows("r", "r1", "r2"));

        var rules = await _sut.GetCompilationRulesAsync("u1", new[] { "tools.git" }, []);

        rules.Select(r => r.Id).Should().BeEquivalentTo("r1", "r2");
    }

    [Fact]
    public async Task FindOpenNeighborsAsync_MapsFrontierNeighbors()
    {
        SetupQuery(NodeRows("n", "n1", "n2"));

        var found = await _sut.FindOpenNeighborsAsync(new[] { "r0" }, "u1");

        found.Select(r => r.Id).Should().BeEquivalentTo("n1", "n2");
    }

    [Fact]
    public async Task GetRuleStatisticsAsync_AggregatesCounts()
    {
        void SetupFor(string marker, params IReadOnlyDictionary<string, object?>[] rows)
            => _cypher.Setup(c => c.QueryAsync(
                    It.Is<string>(q => q.Contains(marker)), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows);

        SetupFor("AS total", new Dictionary<string, object?>
        {
            ["total"] = 10, ["globalRules"] = 6, ["userRules"] = 4, ["withValidator"] = 2,
        });
        SetupFor("AS edges", new Dictionary<string, object?> { ["edges"] = 7 });
        SetupFor("withEmbedding", new Dictionary<string, object?> { ["withEmbedding"] = 3 });
        SetupFor("AS domain",
            new Dictionary<string, object?> { ["domain"] = "csharp", ["cnt"] = 5 },
            new Dictionary<string, object?> { ["domain"] = "security", ["cnt"] = 4 });
        SetupFor("AS type", new Dictionary<string, object?> { ["type"] = "Rule", ["cnt"] = 9 });

        var stats = await _sut.GetRuleStatisticsAsync();

        stats.TotalRules.Should().Be(10);
        stats.GlobalRules.Should().Be(6);
        stats.UserRules.Should().Be(4);
        stats.RulesWithValidators.Should().Be(2);
        stats.TotalEdges.Should().Be(7);
        stats.RulesWithEmbeddings.Should().Be(3);
        stats.RulesByDomain.Should().BeEquivalentTo(new Dictionary<string, int> { ["csharp"] = 5, ["security"] = 4 });
        stats.RulesByType.Should().BeEquivalentTo(new Dictionary<string, int> { ["Rule"] = 9 });
    }

    // ---- Dataset read visibility (ADR-0014) ----

    private CypherGraphStore StoreWithVisibility(DatasetVisibility visibility)
    {
        var permissions = new Mock<IDatasetPermissionService>();
        permissions.Setup(p => p.ResolveVisibility()).Returns(visibility);
        return new CypherGraphStore(_cypher.Object, _identity.Object, timeProvider: null, permissions.Object);
    }

    [Fact]
    public async Task GetRulesAsync_RestrictedVisibility_HidesRulesOutsideVisibleDatasets()
    {
        SetupQuery(NodeRows("r", "git:repo:a", "git:other:b", "hand-authored"));
        var sut = StoreWithVisibility(DatasetVisibility.Restricted(["git:repo"]));

        var rules = await sut.GetRulesAsync();

        // The visible dataset and the ungrouped rule survive; the hidden dataset's rule is filtered out.
        rules.Select(r => r.Id).Should().BeEquivalentTo("git:repo:a", "hand-authored");
    }

    [Fact]
    public async Task GetRuleAsync_RestrictedVisibility_HidesRuleOutsideVisibleDataset()
    {
        SetupQuery(NodeRows("r", "git:other:x"));
        var sut = StoreWithVisibility(DatasetVisibility.Restricted(["git:repo"]));

        (await sut.GetRuleAsync("git:other:x")).Should().BeNull();
    }

    [Fact]
    public async Task GetRuleAsync_RestrictedVisibility_ReturnsRuleInVisibleDataset()
    {
        SetupQuery(NodeRows("r", "git:repo:x"));
        var sut = StoreWithVisibility(DatasetVisibility.Restricted(["git:repo"]));

        (await sut.GetRuleAsync("git:repo:x"))!.Id.Should().Be("git:repo:x");
    }

    [Fact]
    public async Task GetRulesAsync_DefaultService_DoesNotFilterByDataset()
    {
        // The permissive default keeps the read behaviour-neutral even for provenance-prefixed rules.
        SetupQuery(NodeRows("r", "git:repo:a", "git:other:b"));

        (await _sut.GetRulesAsync()).Select(r => r.Id).Should().BeEquivalentTo("git:repo:a", "git:other:b");
    }
}
