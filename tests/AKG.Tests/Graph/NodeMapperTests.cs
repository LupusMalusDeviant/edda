using Edda.AKG.Graph;
using Edda.Core.Models;
using Moq;
using Neo4j.Driver;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// Unit tests for <see cref="NodeMapper"/> — verifies correct mapping of
/// Neo4j nodes and dictionary representations to domain models, including relations.
/// </summary>
public class NodeMapperTests
{
    // ── Temporal validity ───────────────────────────────────────────────

    [Fact]
    public void MapRowObject_WithTemporalProperties_MapsValidityFields()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "expired-rule",
            ["type"] = "Rule",
            ["domain"] = "general",
            ["priority"] = "Medium",
            ["body"] = "body",
            ["validFrom"] = "2026-01-01T00:00:00.0000000+00:00",
            ["validUntil"] = "2026-03-01T00:00:00.0000000+00:00",
            ["invalidatedBy"] = "newer-rule",
        };

        var result = NodeMapper.MapRowObject(dict);

        result.ValidFrom.Should().Be(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
        result.ValidUntil.Should().Be(DateTimeOffset.Parse("2026-03-01T00:00:00+00:00"));
        result.InvalidatedBy.Should().Be("newer-rule");
    }

    [Fact]
    public void MapRowObject_WithoutTemporalProperties_LeavesValidityNull()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "current-rule",
            ["type"] = "Rule",
            ["domain"] = "general",
            ["priority"] = "Medium",
            ["body"] = "body",
        };

        var result = NodeMapper.MapRowObject(dict);

        result.ValidUntil.Should().BeNull();
        result.InvalidatedBy.Should().BeNull();
    }

    // ── MapRowObject with INode ─────────────────────────────────────────

    [Fact]
    public void MapRowObject_WithINodeContainingRelations_MapsRelatesTo()
    {
        var node = CreateNodeWithRelations(
            "test-rule",
            implies: new List<object> { "rule-a", "rule-b" },
            conflictsWith: new List<object> { "rule-c" });

        var result = NodeMapper.MapRowObject(node);

        Assert.NotNull(result.RelatesTo);
        Assert.Equal(["rule-a", "rule-b"], result.RelatesTo!.Implies);
        Assert.Equal(["rule-c"], result.RelatesTo.ConflictsWith);
        Assert.Empty(result.RelatesTo.ExceptionFor);
    }

    [Fact]
    public void MapRowObject_WithINodeContainingRequiresAndSupersedes_MapsAllRelations()
    {
        var node = CreateNodeWithRelations(
            "full-node-rule",
            requires: new List<object> { "req-1", "req-2" },
            supersedes: new List<object> { "old-rule" });

        var result = NodeMapper.MapRowObject(node);

        Assert.NotNull(result.RelatesTo);
        Assert.Equal(["req-1", "req-2"], result.RelatesTo!.Requires);
        Assert.Equal(["old-rule"], result.RelatesTo.Supersedes);
        Assert.Empty(result.RelatesTo.Implies);
        Assert.Empty(result.RelatesTo.ConflictsWith);
        Assert.Empty(result.RelatesTo.ExceptionFor);
    }

    [Fact]
    public void MapRowObject_WithINodeWithoutRelations_ReturnsNullRelatesTo()
    {
        var node = CreateBasicNode("basic-rule");

        var result = NodeMapper.MapRowObject(node);

        Assert.Null(result.RelatesTo);
    }

    // ── MapRowObject with Dictionary ────────────────────────────────────

    [Fact]
    public void MapRowObject_WithDictionaryContainingRelations_MapsRelatesTo()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "dict-rule",
            ["type"] = "Rule",
            ["domain"] = "general",
            ["priority"] = "High",
            ["body"] = "test body",
            ["tags"] = new List<object>(),
            ["implies"] = new List<object> { "target-1" },
            ["conflictsWith"] = new List<object> { "conflict-1" },
            ["requires"] = new List<object> { "req-1", "req-2" },
        };

        var result = NodeMapper.MapRowObject(dict);

        Assert.NotNull(result.RelatesTo);
        Assert.Equal(["target-1"], result.RelatesTo!.Implies);
        Assert.Equal(["conflict-1"], result.RelatesTo.ConflictsWith);
        Assert.Equal(["req-1", "req-2"], result.RelatesTo.Requires);
        Assert.Empty(result.RelatesTo.ExceptionFor);
        Assert.Empty(result.RelatesTo.Supersedes);
    }

    [Fact]
    public void MapRowObject_WithDictionaryWithoutRelations_ReturnsNullRelatesTo()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "plain-rule",
            ["type"] = "Rule",
            ["domain"] = "general",
            ["priority"] = "Medium",
            ["body"] = "body",
            ["tags"] = new List<object>(),
        };

        var result = NodeMapper.MapRowObject(dict);

        Assert.Null(result.RelatesTo);
    }

    // ── MapKnowledgeRow ────────────────────────────────────────────────

    [Fact]
    public void MapKnowledgeRow_WithRelations_MapsRelatesTo()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "kr-rule",
            ["type"] = "Constraint",
            ["domain"] = "security",
            ["priority"] = "Critical",
            ["body"] = "no secrets",
            ["tags"] = new List<object> { "security" },
            ["supersedes"] = new List<object> { "old-rule" },
        };

        var result = NodeMapper.MapKnowledgeRow(dict);

        Assert.NotNull(result);
        Assert.NotNull(result!.RelatesTo);
        Assert.Equal(["old-rule"], result.RelatesTo!.Supersedes);
    }

    [Fact]
    public void MapKnowledgeRow_NullObject_ReturnsNull()
    {
        var result = NodeMapper.MapKnowledgeRow(null);
        Assert.Null(result);
    }

    // ── MapNode (INode) ────────────────────────────────────────────────

    [Fact]
    public void MapNode_WithAllRelationTypes_MapsAllLists()
    {
        var node = CreateNodeWithRelations(
            "full-rule",
            implies: new List<object> { "a" },
            conflictsWith: new List<object> { "b" },
            exceptionFor: new List<object> { "c" },
            requires: new List<object> { "d" },
            supersedes: new List<object> { "e" });

        var result = NodeMapper.MapNode(node);

        Assert.NotNull(result.RelatesTo);
        Assert.Equal(["a"], result.RelatesTo!.Implies);
        Assert.Equal(["b"], result.RelatesTo.ConflictsWith);
        Assert.Equal(["c"], result.RelatesTo.ExceptionFor);
        Assert.Equal(["d"], result.RelatesTo.Requires);
        Assert.Equal(["e"], result.RelatesTo.Supersedes);
    }

    // ── Type mapping ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Rule")]
    [InlineData("Constraint")]
    [InlineData("Guideline")]
    public void MapRowObject_PreservesType(string type)
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "typed-rule",
            ["type"] = type,
            ["domain"] = "general",
            ["priority"] = "Medium",
            ["body"] = "body",
        };

        var result = NodeMapper.MapRowObject(dict);

        Assert.Equal(type, result.Type);
    }

    // ── Tenant scoping ──────────────────────────────────────────────────

    [Fact]
    public void MapRowObject_WithTenantId_MapsIt()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "t-rule", ["type"] = "Rule", ["domain"] = "general",
            ["priority"] = "Medium", ["body"] = "body", ["tenantId"] = "acme",
        };

        NodeMapper.MapRowObject(dict).TenantId.Should().Be("acme");
    }

    [Fact]
    public void MapRowObject_WithoutTenantId_DefaultsToDefaultTenant()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = "t-rule", ["type"] = "Rule", ["domain"] = "general",
            ["priority"] = "Medium", ["body"] = "body",
        };

        NodeMapper.MapRowObject(dict).TenantId.Should().Be(Tenants.DefaultTenantId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static INode CreateBasicNode(string id)
    {
        var mock = new Mock<INode>();
        var props = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "Rule",
            ["domain"] = "general",
            ["priority"] = "Medium",
            ["body"] = "body",
            ["tags"] = new List<object>(),
        };
        mock.SetupGet(n => n.Properties).Returns(props);
        return mock.Object;
    }

    private static INode CreateNodeWithRelations(
        string id,
        List<object>? implies = null,
        List<object>? conflictsWith = null,
        List<object>? exceptionFor = null,
        List<object>? requires = null,
        List<object>? supersedes = null)
    {
        var mock = new Mock<INode>();
        var props = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = "Rule",
            ["domain"] = "general",
            ["priority"] = "High",
            ["body"] = "body",
            ["tags"] = new List<object>(),
        };
        if (implies is not null) props["implies"] = implies;
        if (conflictsWith is not null) props["conflictsWith"] = conflictsWith;
        if (exceptionFor is not null) props["exceptionFor"] = exceptionFor;
        if (requires is not null) props["requires"] = requires;
        if (supersedes is not null) props["supersedes"] = supersedes;
        mock.SetupGet(n => n.Properties).Returns(props);
        return mock.Object;
    }
}
