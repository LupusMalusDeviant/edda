using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using FluentAssertions;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// Unit tests for <see cref="InMemoryCypherExecutor"/>. The queries used here are the verbatim shapes that
/// <see cref="Neo4jKnowledgeGraph"/> issues, so these tests also guard the executor's dispatch against the
/// real call sites.
/// </summary>
public sealed class InMemoryCypherExecutorTests
{
    private readonly ICypherExecutor _sut = new InMemoryCypherExecutor();

    // ── Verbatim query shapes (mirrors Neo4jKnowledgeGraph) ──────────────────────

    private const string UpsertQuery = """
        MERGE (r:Rule {id: $id})
        SET r.type = $type,
            r.domain = $domain,
            r.priority = $priority,
            r.body = $body,
            r.tags = $tags,
            r.ownerId = $ownerId,
            r.implies = $implies,
            r.conflictsWith = $conflictsWith,
            r.exceptionFor = $exceptionFor,
            r.requires = $requires,
            r.supersedes = $supersedes,
            r.related = $related,
            r.concepts = $concepts,
            r.chunkStyle = $chunkStyle,
            r.validFrom = coalesce(r.validFrom, $now)
        """;

    private const string GetRuleQuery =
        "MATCH (r:Rule {id: $ruleId}) WHERE r.ownerId IS NULL OR r.ownerId = $userId RETURN r";

    private const string NeighborsQuery =
        "MATCH (r:Rule {id: $ruleId})-[]-(n:Rule) WHERE n.ownerId IS NULL OR n.ownerId = $userId RETURN n";

    private const string HeadsQuery = """
        MATCH (r:Rule)
        WHERE (r.ownerId IS NULL OR r.ownerId = $userId)
          AND NOT ((r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:') AND size(split(r.id, ':')) >= 3)
        RETURN r
        """;

    private const string StatsMainQuery = """
        MATCH (r:Rule)
        RETURN
            count(r) AS total,
            sum(CASE WHEN r.ownerId IS NULL THEN 1 ELSE 0 END) AS globalRules,
            sum(CASE WHEN r.ownerId IS NOT NULL THEN 1 ELSE 0 END) AS userRules,
            sum(CASE WHEN r.validatorScript IS NOT NULL THEN 1 ELSE 0 END) AS withValidator
        """;

    private const string SubtreeCountQuery = """
        MATCH (r:Rule)
        WHERE r.id = $rootId OR ANY(p IN $prefixes WHERE r.id STARTS WITH p)
        RETURN count(r) AS n
        """;

    private const string SubtreeDeleteQuery = """
        MATCH (r:Rule)
        WHERE r.id = $rootId OR ANY(p IN $prefixes WHERE r.id STARTS WITH p)
        OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
        DETACH DELETE r, c
        """;

    private const string DeleteRuleQuery = """
        MATCH (r:Rule {id: $ruleId})
        OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
        DETACH DELETE r, c
        """;

    private const string InvalidateQuery = """
        MATCH (newer:Rule)
        WHERE newer.validUntil IS NULL AND newer.supersedes IS NOT NULL
        UNWIND newer.supersedes AS olderId
        MATCH (older:Rule {id: olderId})
        WHERE older.validUntil IS NULL AND older.id <> newer.id
        SET older.validUntil = $now, older.invalidatedBy = newer.id
        """;

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private Task Upsert(
        string id, string domain = "csharp", string type = "Rule", string? ownerId = null,
        string[]? tags = null, string[]? supersedes = null, string[]? concepts = null,
        string now = "2026-01-01T00:00:00.0000000+00:00")
        => _sut.ExecuteAsync(UpsertQuery, new
        {
            id,
            type,
            domain,
            priority = "Medium",
            body = $"body {id}",
            tags = tags ?? [],
            ownerId,
            implies = Array.Empty<string>(),
            conflictsWith = Array.Empty<string>(),
            exceptionFor = Array.Empty<string>(),
            requires = Array.Empty<string>(),
            supersedes = supersedes ?? [],
            related = Array.Empty<string>(),
            concepts = concepts ?? [],
            chunkStyle = (string?)null,
            now,
        });

    private async Task<IReadOnlyList<string>> GetRuleIds(string query, object parameters, string column = "r")
    {
        var rows = await _sut.QueryAsync(query, parameters);
        return rows.Select(row => ((IReadOnlyDictionary<string, object?>)row[column]!)["id"] as string ?? "")
                   .ToList();
    }

    // ── Upsert + GetRule ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRule_AfterUpsert_ReturnsRuleProperties()
    {
        await Upsert("rule-1", domain: "security", type: "Policy");

        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "rule-1", userId = (string?)null });

        rows.Should().ContainSingle();
        var r = (IReadOnlyDictionary<string, object?>)rows[0]["r"]!;
        r["id"].Should().Be("rule-1");
        r["domain"].Should().Be("security");
        r["type"].Should().Be("Policy");
    }

    [Fact]
    public async Task GetRule_Missing_ReturnsEmpty()
    {
        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "nope", userId = (string?)null });
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRule_OtherUsersRule_IsOutOfScope()
    {
        await Upsert("owned", ownerId: "user-a");

        var asOwner = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "owned", userId = "user-a" });
        var asOther = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "owned", userId = "user-b" });

        asOwner.Should().ContainSingle();
        asOther.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_Twice_CoalescesValidFrom()
    {
        await Upsert("r", now: "2020-01-01T00:00:00.0000000+00:00");
        await Upsert("r", now: "2099-01-01T00:00:00.0000000+00:00");

        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "r", userId = (string?)null });
        var r = (IReadOnlyDictionary<string, object?>)rows[0]["r"]!;
        r["validFrom"].Should().Be("2020-01-01T00:00:00.0000000+00:00");
    }

    // ── GetRules + filters ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetRules_NoFilter_ReturnsAllInScope()
    {
        await Upsert("a", domain: "csharp");
        await Upsert("b", domain: "security");
        await Upsert("c", ownerId: "someone-else");

        var ids = await GetRuleIds(
            "MATCH (r:Rule) WHERE (r.ownerId IS NULL OR r.ownerId = $userId) RETURN r",
            new { domain = (string?)null, type = (string?)null, tag = (string?)null, userId = (string?)null });

        ids.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task GetRules_DomainAndTagFilters_AreApplied()
    {
        await Upsert("a", domain: "csharp", tags: ["async"]);
        await Upsert("b", domain: "csharp", tags: ["io"]);
        await Upsert("c", domain: "security", tags: ["async"]);

        var byDomain = await GetRuleIds(
            "MATCH (r:Rule) WHERE (r.ownerId IS NULL OR r.ownerId = $userId) AND r.domain = $domain RETURN r",
            new { domain = "csharp", type = (string?)null, tag = (string?)null, userId = (string?)null });
        byDomain.Should().BeEquivalentTo(["a", "b"]);

        var byTag = await GetRuleIds(
            "MATCH (r:Rule) WHERE (r.ownerId IS NULL OR r.ownerId = $userId) AND $tag IN r.tags RETURN r",
            new { domain = (string?)null, type = (string?)null, tag = "async", userId = (string?)null });
        byTag.Should().BeEquivalentTo(["a", "c"]);
    }

    [Fact]
    public async Task GetRules_TypeFilter_IsApplied()
    {
        await Upsert("a", type: "Rule");
        await Upsert("b", type: "Policy");

        var ids = await GetRuleIds(
            "MATCH (r:Rule) WHERE (r.ownerId IS NULL OR r.ownerId = $userId) AND r.type = $type RETURN r",
            new { domain = (string?)null, type = "Policy", tag = (string?)null, userId = (string?)null });

        ids.Should().BeEquivalentTo(["b"]);
    }

    // ── Heads ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRuleHeads_ExcludesNestedGitAndUploadLeaves()
    {
        await Upsert("standalone");
        await Upsert("git-knowledge");
        await Upsert("git:myrepo:src/File.cs"); // nested leaf (3 segments) → excluded
        await Upsert("upload:docs:readme.md");  // nested leaf → excluded

        var ids = await GetRuleIds(HeadsQuery, new { userId = (string?)null });

        ids.Should().BeEquivalentTo(["standalone", "git-knowledge"]);
    }

    // ── Neighbors + edges ────────────────────────────────────────────────────────

    [Fact]
    public async Task FindNeighbors_ReturnsBothEdgeDirections()
    {
        await Upsert("a");
        await Upsert("b");
        await Upsert("c");
        await MergeEdge("a", "IMPLIES", "b"); // a → b
        await MergeEdge("c", "REQUIRES", "a"); // c → a

        var rows = await _sut.QueryAsync(NeighborsQuery, new { ruleId = "a", userId = (string?)null });
        var ids = rows.Select(r => ((IReadOnlyDictionary<string, object?>)r["n"]!)["id"] as string).ToList();

        ids.Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public async Task DeleteEdges_RemovesOnlyMatchingRelType()
    {
        await Upsert("a");
        await Upsert("b");
        await MergeEdge("a", "IMPLIES", "b");
        await MergeEdge("a", "REQUIRES", "b");

        await _sut.ExecuteAsync("MATCH (s:Rule {id: $sourceId})-[e:IMPLIES]->() DELETE e", new { sourceId = "a" });

        var rows = await _sut.QueryAsync(NeighborsQuery, new { ruleId = "a", userId = (string?)null });
        rows.Should().ContainSingle(); // REQUIRES edge to b still present → b once
    }

    [Fact]
    public async Task BatchedEdgeUpsert_ClosesDroppedEdges_AndCreatesTargets()
    {
        await Upsert("a");
        await Upsert("b");
        await Upsert("c");
        await Upsert("x");
        await BatchReplaceEdges("a", "IMPLIES", ["x"], T1); // pre-existing edge the batch must drop

        await BatchReplaceEdges("a", "IMPLIES", ["b", "c"], T2);

        // C9: the retrieval expansion only traverses open edges — x was temporally closed at T2 …
        var expanded = await GetRuleIds(ExpandQuery, new { frontier = new[] { "a" }, userId = (string?)null, now = T3 }, column: "n");
        expanded.Should().BeEquivalentTo(["b", "c"]);

        // … but the (unfiltered) neighbor view still shows the closed edge as history.
        var neighbors = await GetRuleIds(NeighborsQuery, new { ruleId = "a", userId = (string?)null }, column: "n");
        neighbors.Should().BeEquivalentTo(["b", "c", "x"]);
    }

    [Fact]
    public async Task BatchedEdgeUpsert_EmptyTargetList_ClosesAllOpenEdges()
    {
        await Upsert("a");
        await Upsert("b");
        await BatchReplaceEdges("a", "IMPLIES", ["b"], T1);

        await BatchReplaceEdges("a", "IMPLIES", [], T2);

        var expanded = await GetRuleIds(ExpandQuery, new { frontier = new[] { "a" }, userId = (string?)null, now = T3 }, column: "n");
        expanded.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchedEdgeUpsert_RedeclaredTarget_ReopensClosedEdge()
    {
        await Upsert("a");
        await Upsert("b");
        await BatchReplaceEdges("a", "IMPLIES", ["b"], T1);
        await BatchReplaceEdges("a", "IMPLIES", [], T2);   // closed …

        await BatchReplaceEdges("a", "IMPLIES", ["b"], T3); // … and re-declared → re-opened

        var expanded = await GetRuleIds(ExpandQuery, new { frontier = new[] { "a" }, userId = (string?)null, now = T3 }, column: "n");
        expanded.Should().BeEquivalentTo(["b"]);
    }

    [Fact]
    public async Task InvalidateSuperseded_ClosesEdgesOfInvalidatedRule_KeepsIncomingSupersedes()
    {
        // old --RELATED--> other; new --SUPERSEDES--> old, new.supersedes = [old].
        await Upsert("old");
        await Upsert("other");
        await BatchReplaceEdges("old", "RELATED", ["other"], T1);
        await Upsert("new", supersedes: ["old"]);
        await BatchReplaceEdges("new", "SUPERSEDES", ["old"], T1);

        await _sut.ExecuteAsync(InvalidateQuery, new { now = T2 });

        // The invalidated fact's RELATED edge is closed → 'other' no longer expands to 'old' …
        var fromOther = await GetRuleIds(ExpandQuery, new { frontier = new[] { "other" }, userId = (string?)null, now = T3 }, column: "n");
        fromOther.Should().BeEmpty();

        // … while the incoming SUPERSEDES edge stays open (it documents the supersession) …
        var fromOld = await GetRuleIds(ExpandQuery, new { frontier = new[] { "old" }, userId = (string?)null, now = T3 }, column: "n");
        fromOld.Should().BeEquivalentTo(["new"]);

        // … and the unfiltered neighbor view keeps the full history.
        var neighbors = await GetRuleIds(NeighborsQuery, new { ruleId = "old", userId = (string?)null }, column: "n");
        neighbors.Should().BeEquivalentTo(["new", "other"]);
    }

    // ── E10: soft delete / recycle bin ───────────────────────────────────────────

    private const string SoftDeleteQuery = """
        MATCH (r:Rule {id: $ruleId})
        SET r.deletedAt = $now,
            r.deletedBy = $userId,
            r.validUntil = coalesce(r.validUntil, $now)
        """;

    private const string RestoreQuery = """
        MATCH (r:Rule {id: $ruleId})
        WHERE r.deletedAt IS NOT NULL
        SET r.validUntil = CASE WHEN r.validUntil = r.deletedAt THEN null ELSE r.validUntil END,
            r.deletedAt = null,
            r.deletedBy = null
        """;

    private const string ListDeletedQuery = """
        MATCH (r:Rule)
        WHERE r.deletedAt IS NOT NULL AND ($isAdmin OR r.ownerId = $userId)
        RETURN r
        """;

    private Task SoftDelete(string id, string userId = "u1", string now = T2)
        => _sut.ExecuteAsync(SoftDeleteQuery, new { ruleId = id, userId, now });

    [Fact]
    public async Task SoftDelete_RuleDisappearsFromActiveViews_AppearsInBin()
    {
        await Upsert("a", ownerId: "u1");

        await SoftDelete("a");

        var active = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "a", userId = "u1" });
        active.Should().BeEmpty("active views hide soft-deleted rules");

        var bin = await GetRuleIds(ListDeletedQuery, new { userId = "u1", isAdmin = false });
        bin.Should().BeEquivalentTo(["a"]);
    }

    [Fact]
    public async Task Restore_DeletedRule_VisibleAgain_WithValidUntilCleared()
    {
        await Upsert("a", ownerId: "u1");
        await SoftDelete("a", now: T2); // validUntil = deletedAt = T2 (came from the delete)

        await _sut.ExecuteAsync(RestoreQuery, new { ruleId = "a" });

        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "a", userId = "u1" });
        rows.Should().ContainSingle();
        var props = (IReadOnlyDictionary<string, object?>)rows[0]["r"]!;
        props.GetValueOrDefault("validUntil").Should().BeNull("the delete-born validUntil is cleared on restore");
        props.GetValueOrDefault("deletedAt").Should().BeNull();
    }

    [Fact]
    public async Task Restore_RuleWithSupersedeValidUntil_KeepsSupersedeTimestamp()
    {
        // old is superseded at T1 (validUntil = T1), then soft-deleted at T2 (coalesce keeps T1).
        await Upsert("old", ownerId: "u1");
        await Upsert("newer", supersedes: ["old"]);
        await _sut.ExecuteAsync(InvalidateQuery, new { now = T1 });
        await SoftDelete("old", now: T2);

        await _sut.ExecuteAsync(RestoreQuery, new { ruleId = "old" });

        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "old", userId = "u1" });
        rows.Should().ContainSingle();
        var props = (IReadOnlyDictionary<string, object?>)rows[0]["r"]!;
        props.GetValueOrDefault("validUntil").Should().Be(T1, "a supersede timestamp survives the restore");
        props.GetValueOrDefault("deletedAt").Should().BeNull();
    }

    [Fact]
    public async Task Purge_UsesExistingDeleteShape_RemovesRuleForGood()
    {
        await Upsert("a", ownerId: "u1");
        await SoftDelete("a");

        await _sut.ExecuteAsync(DeleteRuleQuery, new { ruleId = "a" });

        var bin = await GetRuleIds(ListDeletedQuery, new { userId = "u1", isAdmin = false });
        bin.Should().BeEmpty();
        var active = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "a", userId = "u1" });
        active.Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_GetRule_RoundTripsConcepts()
    {
        // B5: the concepts property survives the in-memory round-trip like on Neo4j.
        await Upsert("c-rule", concepts: ["password", "secret"]);

        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "c-rule", userId = (string?)null });

        rows.Should().ContainSingle();
        var props = (IReadOnlyDictionary<string, object?>)rows[0]["r"]!;
        var concepts = props.GetValueOrDefault("concepts") as string[];
        concepts.Should().BeEquivalentTo(["password", "secret"]);
    }

    private const string T1 = "2026-01-01T00:00:00.0000000+00:00";
    private const string T2 = "2026-02-01T00:00:00.0000000+00:00";
    private const string T3 = "2026-03-01T00:00:00.0000000+00:00";

    /// <summary>C9 GraphExpander shape: only temporally open edges are traversed.</summary>
    private const string ExpandQuery = """
        MATCH (r:Rule)-[e]-(n:Rule)
        WHERE r.id IN $frontier AND (n.ownerId IS NULL OR n.ownerId = $userId)
          AND (e.validUntil IS NULL OR e.validUntil > $now)
        RETURN DISTINCT n
        """;

    /// <summary>The C9 temporal replace shape issued by both edge writers.</summary>
    private Task BatchReplaceEdges(string source, string rel, string[] targets, string now)
        => _sut.ExecuteAsync(
            "MATCH (s:Rule {id: $sourceId}) " +
            $"OPTIONAL MATCH (s)-[stale:{rel}]->(t0:Rule) " +
            "WHERE stale.validUntil IS NULL AND NOT t0.id IN $targetIds " +
            "SET stale.validUntil = $now " +
            "WITH DISTINCT s " +
            "UNWIND $targetIds AS targetId " +
            "MATCH (t:Rule {id: targetId}) " +
            $"MERGE (s)-[e:{rel}]->(t) " +
            "ON CREATE SET e.validFrom = $now " +
            "SET e.validUntil = null",
            new { sourceId = source, targetIds = targets, now });

    private Task MergeEdge(string source, string rel, string target)
        => _sut.ExecuteAsync(
            $"MATCH (s:Rule {{id: $sourceId}}), (t:Rule {{id: $targetId}}) MERGE (s)-[:{rel}]->(t)",
            new { sourceId = source, targetId = target });

    // ── Delete ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRule_RemovesRuleAndItsEdges()
    {
        await Upsert("a");
        await Upsert("b");
        await MergeEdge("a", "IMPLIES", "b");

        await _sut.ExecuteAsync(DeleteRuleQuery, new { ruleId = "a" });

        var a = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "a", userId = (string?)null });
        a.Should().BeEmpty();
        var neighborsOfB = await _sut.QueryAsync(NeighborsQuery, new { ruleId = "b", userId = (string?)null });
        neighborsOfB.Should().BeEmpty(); // edge to deleted 'a' is gone
    }

    [Fact]
    public async Task DeleteSubtree_RemovesRootAndPrefixedDescendants()
    {
        await Upsert("git-knowledge");
        await Upsert("git:repo:file1");
        await Upsert("git:repo:file2");
        await Upsert("unrelated");

        var prefixes = new[] { "git:", "git-host:", "git-group:" };

        var countRows = await _sut.QueryAsync(SubtreeCountQuery, new { rootId = "git-knowledge", prefixes });
        countRows[0]["n"].Should().Be(3);

        await _sut.ExecuteAsync(SubtreeDeleteQuery, new { rootId = "git-knowledge", prefixes });

        var remaining = await GetRuleIds(
            "MATCH (r:Rule) WHERE (r.ownerId IS NULL OR r.ownerId = $userId) RETURN r",
            new { domain = (string?)null, type = (string?)null, tag = (string?)null, userId = (string?)null });
        remaining.Should().BeEquivalentTo(["unrelated"]);
    }

    // ── Stats ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StatsMain_CountsTotalGlobalAndUser()
    {
        await Upsert("g1");
        await Upsert("g2");
        await Upsert("u1", ownerId: "user-a");

        var rows = await _sut.QueryAsync(StatsMainQuery);
        var s = rows[0];

        s["total"].Should().Be(3);
        s["globalRules"].Should().Be(2);
        s["userRules"].Should().Be(1);
        s["withValidator"].Should().Be(0);
    }

    [Fact]
    public async Task Stats_Edges_Embedded_Domain_Type()
    {
        await Upsert("a", domain: "csharp", type: "Rule");
        await Upsert("b", domain: "security", type: "Policy");
        await MergeEdge("a", "IMPLIES", "b");

        (await _sut.QueryAsync("MATCH ()-[e]->() WHERE type(e) <> 'HAS_CHUNK' RETURN count(e) AS edges"))[0]["edges"]
            .Should().Be(1);
        (await _sut.QueryAsync("MATCH (c:RuleChunk) RETURN count(DISTINCT c.parentId) AS withEmbedding"))[0]["withEmbedding"]
            .Should().Be(0);

        var domainRows = await _sut.QueryAsync("MATCH (r:Rule) RETURN r.domain AS domain, count(r) AS cnt");
        domainRows.Should().HaveCount(2);
        var typeRows = await _sut.QueryAsync("MATCH (r:Rule) RETURN r.type AS type, count(r) AS cnt");
        typeRows.Should().HaveCount(2);
    }

    // ── Invalidate superseded + embedding-prop maintenance ──────────────────────

    [Fact]
    public async Task InvalidateSuperseded_SetsValidUntilOnOlderRule()
    {
        await Upsert("old");
        await Upsert("new", supersedes: ["old"]);

        await _sut.ExecuteAsync(InvalidateQuery, new { now = "2026-06-30T00:00:00.0000000+00:00" });

        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "old", userId = (string?)null });
        var old = (IReadOnlyDictionary<string, object?>)rows[0]["r"]!;
        old["validUntil"].Should().Be("2026-06-30T00:00:00.0000000+00:00");
        old["invalidatedBy"].Should().Be("new");
    }

    [Fact]
    public async Task RemoveBodyHash_And_ResetEmbeddingProps_DoNotThrow()
    {
        await Upsert("a");

        await _sut.ExecuteAsync("MATCH (r:Rule {id: $id}) REMOVE r.bodyHash", new { id = "a" });
        await _sut.ExecuteAsync("MATCH (:Rule)-[:HAS_CHUNK]->(c:RuleChunk) DETACH DELETE c");
        await _sut.ExecuteAsync(
            "MATCH (r:Rule) WHERE r.bodyHash IS NOT NULL OR r.embedding IS NOT NULL OR r.embedAttempts IS NOT NULL "
            + "REMOVE r.bodyHash, r.embedding, r.embedAttempts");

        var rows = await _sut.QueryAsync(GetRuleQuery, new { ruleId = "a", userId = (string?)null });
        rows.Should().ContainSingle(); // rule survives; prop maintenance is a no-op for coverage
    }

    // ── Unknown queries ──────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryAsync_UnknownShape_Throws()
    {
        var act = () => _sut.QueryAsync("MATCH (x:Thing) RETURN x");
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownShape_Throws()
    {
        var act = () => _sut.ExecuteAsync("CREATE (x:Thing)");
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
