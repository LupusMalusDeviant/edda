using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using FluentAssertions;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// Stage-2 unit tests for <see cref="InMemoryCypherExecutor"/>: WorldKnowledge, domains/seeding,
/// validator, and the context-compilation reads (LoadRules, GraphExpander). Queries are the verbatim
/// shapes issued by the seeders, DomainManager, GraphValidator, and ContextCompiler.
/// </summary>
public sealed class InMemoryCypherExecutorStage2Tests
{
    private readonly ICypherExecutor _sut = new InMemoryCypherExecutor();

    private const string UpsertQuery = """
        MERGE (r:Rule {id: $id})
        SET r.type = $type, r.domain = $domain, r.priority = $priority, r.body = $body, r.tags = $tags,
            r.ownerId = $ownerId, r.implies = $implies, r.conflictsWith = $conflictsWith,
            r.exceptionFor = $exceptionFor, r.requires = $requires, r.supersedes = $supersedes,
            r.related = $related, r.chunkStyle = $chunkStyle, r.validFrom = coalesce(r.validFrom, $now)
        """;

    private const string LoadRulesQuery =
        "MATCH (r:Rule) WHERE (r.ownerId IS NULL OR r.ownerId = $userId) " +
        "AND (NOT r.domain STARTS WITH 'tools.' OR r.domain IN $toolboxes) " +
        "AND (r.validUntil IS NULL OR r.validUntil > $now) " +
        "AND (size($prefixes) = 0 OR NOT (r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:') " +
        "OR any(p IN $prefixes WHERE r.id STARTS WITH p)) RETURN r";

    private const string InvalidateQuery =
        "MATCH (newer:Rule) WHERE newer.validUntil IS NULL AND newer.supersedes IS NOT NULL " +
        "UNWIND newer.supersedes AS olderId MATCH (older:Rule {id: olderId}) " +
        "WHERE older.validUntil IS NULL AND older.id <> newer.id " +
        "SET older.validUntil = $now, older.invalidatedBy = newer.id";

    private const string DomainTreeManagerQuery =
        "MATCH (d:Domain) OPTIONAL MATCH (d)<-[:HAS_SUBDOMAIN]-(parent:Domain) RETURN d, parent.name AS parentName";

    private const string DomainTreeResolverQuery =
        "MATCH (d:Domain) OPTIONAL MATCH (d)<-[:HAS_SUBDOMAIN]-(parent:Domain) " +
        "RETURN d.name AS name, d.label AS label, parent.name AS parent";

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private Task UpsertRule(string id, string domain = "csharp", string? ownerId = null,
        string[]? implies = null, string[]? supersedes = null)
        => _sut.ExecuteAsync(UpsertQuery, new
        {
            id, type = "Rule", domain, priority = "Medium", body = "b", tags = Array.Empty<string>(),
            ownerId, implies = implies ?? [], conflictsWith = Array.Empty<string>(),
            exceptionFor = Array.Empty<string>(), requires = Array.Empty<string>(),
            supersedes = supersedes ?? [], related = Array.Empty<string>(),
            chunkStyle = (string?)null, now = "2026-01-01T00:00:00.0000000+00:00",
        });

    private Task MergeEdge(string source, string rel, string target)
        => _sut.ExecuteAsync(
            $"MATCH (s:Rule {{id: $sourceId}}), (t:Rule {{id: $targetId}}) MERGE (s)-[:{rel}]->(t)",
            new { sourceId = source, targetId = target });

    private Task UpsertWk(string id, string domain, string[] tags)
        => _sut.ExecuteAsync(
            "MERGE (w:WorldKnowledge {id: $id}) SET w.type = $type, w.domain = $domain, " +
            "w.priority = $priority, w.body = $body, w.tags = $tags",
            new { id, type = "WorldKnowledge", domain, priority = "Low", body = "b", tags });

    private async Task<int> Scalar(string query, object? parameters, string column)
        => Convert.ToInt32((await _sut.QueryAsync(query, parameters))[0][column]);

    private async Task<IReadOnlyList<string>> RuleIds(string query, object parameters, string column = "r")
        => (await _sut.QueryAsync(query, parameters))
            .Select(row => ((IReadOnlyDictionary<string, object?>)row[column]!)["id"] as string ?? "")
            .ToList();

    // ── WorldKnowledge ───────────────────────────────────────────────────────────

    [Fact]
    public async Task WorldKnowledge_Merge_Count_Delete()
    {
        await UpsertWk("wk-1", "security", ["auth"]);
        await UpsertWk("wk-2", "coding", ["async"]);

        (await Scalar("MATCH (w:WorldKnowledge) RETURN count(w) AS n", null, "n")).Should().Be(2);

        await _sut.ExecuteAsync("MATCH (w:WorldKnowledge) DETACH DELETE w");
        (await Scalar("MATCH (w:WorldKnowledge) RETURN count(w) AS n", null, "n")).Should().Be(0);
    }

    [Fact]
    public async Task WorldKnowledge_Fetch_MatchesConceptByDomainOrTag_CapsAtTen()
    {
        await UpsertWk("wk-sec", "security", ["auth"]);
        await UpsertWk("wk-code", "coding", ["async"]);

        const string fetch =
            "MATCH (w:WorldKnowledge) WHERE any(c IN $concepts WHERE toLower(w.domain) CONTAINS toLower(c) " +
            "OR any(t IN w.tags WHERE toLower(t) CONTAINS toLower(c))) RETURN w LIMIT 10";

        var byDomain = await _sut.QueryAsync(fetch, new { concepts = new[] { "SEC" } });
        byDomain.Should().ContainSingle();
        ((IReadOnlyDictionary<string, object?>)byDomain[0]["w"]!)["id"].Should().Be("wk-sec");

        var byTag = await _sut.QueryAsync(fetch, new { concepts = new[] { "async" } });
        ((IReadOnlyDictionary<string, object?>)byTag[0]["w"]!)["id"].Should().Be("wk-code");

        var noMatch = await _sut.QueryAsync(fetch, new { concepts = new[] { "zzz" } });
        noMatch.Should().BeEmpty();
    }

    // ── Domains / seeding ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedToolDomains_Root_And_Toolbox_BuildHierarchy()
    {
        // Root seed (literal-valued MERGE) — mirrors WorldKnowledgeSeedHostedService part 1.
        await _sut.ExecuteAsync(
            "MERGE (t:Domain {name: 'tools'}) SET t.label = 'Tool-Dokumentation', t.isCore = true " +
            "WITH t MERGE (ct:Domain {name: 'custom-tools'}) SET ct.label = 'Custom Tools', ct.isCore = true " +
            "WITH t, ct MERGE (t)-[:HAS_SUBDOMAIN]->(ct)");

        // Toolbox loop entry (param-valued MERGE).
        await _sut.ExecuteAsync(
            "MERGE (t:Domain {name: 'tools'}) WITH t MERGE (tb:Domain {name: $name}) " +
            "SET tb.label = $label, tb.isCore = true, tb.description = 'Toolbox: ' + $label " +
            "MERGE (t)-[:HAS_SUBDOMAIN]->(tb)",
            new { name = "tools.web", label = "Web" });

        (await Scalar("MATCH (d:Domain {name: $name}) RETURN count(d) AS cnt", new { name = "tools" }, "cnt"))
            .Should().Be(1);

        var tree = await _sut.QueryAsync(DomainTreeManagerQuery);
        tree.Should().HaveCount(3); // tools, custom-tools, tools.web

        // custom-tools and tools.web hang under 'tools'.
        string? ParentOf(string domainName) => tree
            .Where(r => ((IReadOnlyDictionary<string, object?>)r["d"]!)["name"] as string == domainName)
            .Select(r => r["parentName"] as string).FirstOrDefault();
        ParentOf("custom-tools").Should().Be("tools");
        ParentOf("tools.web").Should().Be("tools");
        ParentOf("tools").Should().BeNull();
    }

    [Fact]
    public async Task CreateDomain_WithParent_LinksEdge_ResolverTreeExposesIt()
    {
        await _sut.ExecuteAsync(
            "MERGE (t:Domain {name: 'tools'}) SET t.label = 'x', t.isCore = true WITH t " +
            "MERGE (ct:Domain {name: 'custom-tools'}) SET ct.label = 'y', ct.isCore = true " +
            "WITH t, ct MERGE (t)-[:HAS_SUBDOMAIN]->(ct)");

        await _sut.ExecuteAsync(
            "MERGE (d:Domain {name: $name}) SET d.label = $label, d.description = $description, " +
            "d.ownerId = $ownerId, d.isCore = false WITH d OPTIONAL MATCH (parent:Domain {name: $parentName}) " +
            "WITH d, parent FOREACH (_ IN CASE WHEN parent IS NOT NULL THEN [1] ELSE [] END " +
            "| MERGE (parent)-[:HAS_SUBDOMAIN]->(d))",
            new { name = "mydomain", label = "Mine", description = "desc", ownerId = "user-a", parentName = "tools" });

        var resolver = await _sut.QueryAsync(DomainTreeResolverQuery);
        var mine = resolver.Single(r => r["name"] as string == "mydomain");
        mine["label"].Should().Be("Mine");
        mine["parent"].Should().Be("tools");
    }

    [Fact]
    public async Task DeleteDomain_RemovesDomainAndEdges()
    {
        await _sut.ExecuteAsync("MERGE (d:Domain {name: $name}) SET d.label = $label, d.description = $description, " +
            "d.ownerId = $ownerId, d.isCore = false WITH d OPTIONAL MATCH (parent:Domain {name: $parentName}) " +
            "WITH d, parent FOREACH (_ IN CASE WHEN parent IS NOT NULL THEN [1] ELSE [] END " +
            "| MERGE (parent)-[:HAS_SUBDOMAIN]->(d))",
            new { name = "temp", label = "T", description = "d", ownerId = (string?)null, parentName = (string?)null });

        (await Scalar("MATCH (d:Domain {name: $name}) RETURN count(d) AS cnt", new { name = "temp" }, "cnt"))
            .Should().Be(1);

        await _sut.ExecuteAsync("MATCH (d:Domain {name: $name}) DETACH DELETE d", new { name = "temp" });

        (await Scalar("MATCH (d:Domain {name: $name}) RETURN count(d) AS cnt", new { name = "temp" }, "cnt"))
            .Should().Be(0);
    }

    // ── Validator ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cycles_DetectsImpliesCycle_ZeroWhenAcyclic()
    {
        await UpsertRule("a");
        await UpsertRule("b");
        const string cycleQuery = "MATCH p=(r:Rule)-[:IMPLIES*2..]->(r) RETURN count(p) AS cycles LIMIT 1";

        (await Scalar(cycleQuery, null, "cycles")).Should().Be(0);

        await MergeEdge("a", "IMPLIES", "b");
        await MergeEdge("b", "IMPLIES", "a"); // a → b → a

        (await Scalar(cycleQuery, null, "cycles")).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Validator_AllRuleIds_And_DanglingImplies()
    {
        await UpsertRule("a", implies: ["ghost"]);
        await UpsertRule("b");

        var ids = (await _sut.QueryAsync("MATCH (r:Rule) RETURN r.id AS id"))
            .Select(r => r["id"] as string).ToList();
        ids.Should().BeEquivalentTo(["a", "b"]);

        var implies = await _sut.QueryAsync(
            "MATCH (r:Rule) WHERE r.implies IS NOT NULL RETURN r.id AS id, r.implies AS implies");
        var forA = implies.Single(r => r["id"] as string == "a");
        ((string[])forA["implies"]!).Should().Contain("ghost");
    }

    // ── ContextCompiler LoadRules ──────────────────────────────────────────────────

    [Fact]
    public async Task LoadRules_ToolboxFilter_HidesInactiveToolDomains()
    {
        await UpsertRule("plain", domain: "csharp");
        await UpsertRule("web", domain: "tools.web");

        var withoutToolbox = await RuleIds(LoadRulesQuery, new
        {
            userId = (string?)null, toolboxes = Array.Empty<string>(),
            now = "2026-06-30T00:00:00.0000000+00:00", prefixes = Array.Empty<string>(),
        });
        withoutToolbox.Should().BeEquivalentTo(["plain"]);

        var withToolbox = await RuleIds(LoadRulesQuery, new
        {
            userId = (string?)null, toolboxes = new[] { "tools.web" },
            now = "2026-06-30T00:00:00.0000000+00:00", prefixes = Array.Empty<string>(),
        });
        withToolbox.Should().BeEquivalentTo(["plain", "web"]);
    }

    [Fact]
    public async Task LoadRules_ExcludesExpiredRules()
    {
        await UpsertRule("old");
        await UpsertRule("new", supersedes: ["old"]);
        // Invalidate 'old' with a past validUntil.
        await _sut.ExecuteAsync(InvalidateQuery, new { now = "2000-01-01T00:00:00.0000000+00:00" });

        var ids = await RuleIds(LoadRulesQuery, new
        {
            userId = (string?)null, toolboxes = Array.Empty<string>(),
            now = "2026-06-30T00:00:00.0000000+00:00", prefixes = Array.Empty<string>(),
        });

        ids.Should().Contain("new");
        ids.Should().NotContain("old"); // validUntil 2000 < now 2026 → expired
    }

    [Fact]
    public async Task LoadRules_PrefixScope_LimitsGitLeavesToMatchingPrefix()
    {
        await UpsertRule("normal");
        await UpsertRule("git:repo:file");

        var noPrefixes = await RuleIds(LoadRulesQuery, new
        {
            userId = (string?)null, toolboxes = Array.Empty<string>(),
            now = "2026-06-30T00:00:00.0000000+00:00", prefixes = Array.Empty<string>(),
        });
        noPrefixes.Should().BeEquivalentTo(["normal", "git:repo:file"]);

        var otherPrefix = await RuleIds(LoadRulesQuery, new
        {
            userId = (string?)null, toolboxes = Array.Empty<string>(),
            now = "2026-06-30T00:00:00.0000000+00:00", prefixes = new[] { "git:other" },
        });
        otherPrefix.Should().BeEquivalentTo(["normal"]); // git leaf excluded, no matching prefix
    }

    // ── GraphExpander ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Expand_ReturnsFrontierNeighborsBothDirections()
    {
        await UpsertRule("a");
        await UpsertRule("b");
        await UpsertRule("c");
        await MergeEdge("a", "IMPLIES", "b"); // a → b
        await MergeEdge("c", "REQUIRES", "a"); // c → a

        var rows = await _sut.QueryAsync(
            "MATCH (r:Rule)-[]-(n:Rule) WHERE r.id IN $frontier AND (n.ownerId IS NULL OR n.ownerId = $userId) " +
            "RETURN DISTINCT n",
            new { frontier = new[] { "a" }, userId = (string?)null });

        var ids = rows.Select(r => ((IReadOnlyDictionary<string, object?>)r["n"]!)["id"] as string).ToList();
        ids.Should().BeEquivalentTo(["b", "c"]);
    }
}
