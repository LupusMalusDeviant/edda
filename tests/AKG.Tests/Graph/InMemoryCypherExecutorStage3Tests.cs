using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using FluentAssertions;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// Stage-3 unit tests for <see cref="InMemoryCypherExecutor"/>: the entity layer (F49), embedding/head
/// coverage (GraphStats), and the embedding-gated shapes that must degrade to no-ops / empty results in
/// memory mode (which stores no chunks or vectors). Queries mirror the verbatim shapes issued by
/// Neo4jEntityStore, Neo4jEmbeddingCache, Neo4jHeadVectorStore, and SemanticBooster.
/// </summary>
public sealed class InMemoryCypherExecutorStage3Tests
{
    private readonly ICypherExecutor _sut = new InMemoryCypherExecutor();

    private const string UpsertRuleQuery = """
        MERGE (r:Rule {id: $id})
        SET r.type = $type, r.domain = $domain, r.priority = $priority, r.body = $body, r.tags = $tags,
            r.ownerId = $ownerId, r.implies = $implies, r.conflictsWith = $conflictsWith,
            r.exceptionFor = $exceptionFor, r.requires = $requires, r.supersedes = $supersedes,
            r.related = $related, r.chunkStyle = $chunkStyle, r.validFrom = coalesce(r.validFrom, $now)
        """;

    private const string EntityIngestQuery =
        "UNWIND $entities AS ent MERGE (e:Entity {ownerId: $ownerId, normalizedName: ent.normalizedName}) " +
        "ON CREATE SET e.id = ent.id, e.name = ent.name, e.type = ent.type, e.description = ent.description, " +
        "e.sourceType = $sourceType, e.mentions = 1, e.createdAt = $now, e.updatedAt = $now " +
        "ON MATCH SET e.mentions = coalesce(e.mentions, 0) + 1, e.updatedAt = $now";

    private const string RelationIngestQuery =
        "UNWIND $relations AS rel MATCH (s:Entity {ownerId: $ownerId, normalizedName: rel.sourceNorm}) " +
        "MATCH (t:Entity {ownerId: $ownerId, normalizedName: rel.targetNorm}) MERGE (s)-[r:RELATES_TO]->(t) " +
        "ON CREATE SET r.weight = 1 ON MATCH SET r.weight = coalesce(r.weight, 0) + 1";

    private const string EntityFindQuery =
        "UNWIND $terms AS term MATCH (e:Entity) WHERE ($userId IS NULL OR e.ownerId = $userId) " +
        "AND toLower(e.name) CONTAINS term RETURN DISTINCT e.name AS name, e.type AS type, " +
        "e.description AS description, coalesce(e.mentions, 0) AS mentions LIMIT $limit";

    private const string EntityRelatedQuery =
        "MATCH (e:Entity {normalizedName: $nname})-[:RELATES_TO]-(other:Entity) " +
        "WHERE ($userId IS NULL OR e.ownerId = $userId) RETURN DISTINCT other.name AS name, other.type AS type, " +
        "other.description AS description, coalesce(other.mentions, 0) AS mentions LIMIT $limit";

    private const string EmbeddingCoverageQuery =
        "MATCH (r:Rule) OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk) WITH r, count(c) AS chunks " +
        "RETURN count(CASE WHEN chunks > 0 THEN 1 END) AS embedded, " +
        "count(CASE WHEN chunks = 0 AND coalesce(r.embedAttempts, 0) < $maxAttempts THEN 1 END) AS pending, " +
        "count(CASE WHEN chunks = 0 AND coalesce(r.embedAttempts, 0) >= $maxAttempts THEN 1 END) AS failed, " +
        "count(r) AS total";

    private const string HeadCoverageQuery =
        "MATCH (r:Rule) WHERE (r.id STARTS WITH 'git:' OR r.id STARTS WITH 'upload:') " +
        "AND size(split(r.id, ':')) = 2 OPTIONAL MATCH (hv:HeadVector {headId: r.id}) " +
        "WITH r, count(hv) AS vectors RETURN count(r) AS totalHeads, " +
        "count(CASE WHEN vectors > 0 THEN 1 END) AS withVectors";

    private Task UpsertRule(string id, string domain = "csharp")
        => _sut.ExecuteAsync(UpsertRuleQuery, new
        {
            id, type = "Rule", domain, priority = "Medium", body = "b", tags = Array.Empty<string>(),
            ownerId = (string?)null, implies = Array.Empty<string>(), conflictsWith = Array.Empty<string>(),
            exceptionFor = Array.Empty<string>(), requires = Array.Empty<string>(),
            supersedes = Array.Empty<string>(), related = Array.Empty<string>(),
            chunkStyle = (string?)null, now = "2026-01-01T00:00:00.0000000+00:00",
        });

    // ── Entity layer (F49) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Entities_Ingest_Then_Find_ByNameSubstring()
    {
        await _sut.ExecuteAsync(EntityIngestQuery, new
        {
            entities = new[]
            {
                new { id = "e1", name = "Alpha", normalizedName = "alpha", type = "Concept", description = "d1" },
                new { id = "e2", name = "Beta", normalizedName = "beta", type = "Concept", description = "d2" },
            },
            ownerId = (string?)null, sourceType = "manual", now = "2026-01-01T00:00:00.0000000+00:00",
        });

        var found = await _sut.QueryAsync(EntityFindQuery,
            new { terms = new[] { "alph" }, userId = (string?)null, limit = 10 });

        found.Should().ContainSingle();
        found[0]["name"].Should().Be("Alpha");
        found[0]["mentions"].Should().Be(1);
    }

    [Fact]
    public async Task Entities_Related_ReturnsNeighborsAcrossRelatesTo()
    {
        await _sut.ExecuteAsync(EntityIngestQuery, new
        {
            entities = new[]
            {
                new { id = "e1", name = "Alpha", normalizedName = "alpha", type = "C", description = "d1" },
                new { id = "e2", name = "Beta", normalizedName = "beta", type = "C", description = "d2" },
            },
            ownerId = (string?)null, sourceType = "manual", now = "2026-01-01T00:00:00.0000000+00:00",
        });
        await _sut.ExecuteAsync(RelationIngestQuery, new
        {
            relations = new[] { new { sourceNorm = "alpha", targetNorm = "beta" } },
            ownerId = (string?)null, now = "2026-01-01T00:00:00.0000000+00:00",
        });

        var related = await _sut.QueryAsync(EntityRelatedQuery,
            new { nname = "alpha", userId = (string?)null, limit = 10 });

        related.Should().ContainSingle();
        related[0]["name"].Should().Be("Beta");
    }

    [Fact]
    public async Task Entities_Find_NoEntities_ReturnsEmpty()
    {
        var found = await _sut.QueryAsync(EntityFindQuery,
            new { terms = new[] { "anything" }, userId = (string?)null, limit = 10 });
        found.Should().BeEmpty();
    }

    // ── Coverage (GraphStats) ───────────────────────────────────────────────────

    [Fact]
    public async Task EmbeddingCoverage_MemoryMode_NothingEmbedded()
    {
        await UpsertRule("a");
        await UpsertRule("b");

        var row = (await _sut.QueryAsync(EmbeddingCoverageQuery, new { maxAttempts = 5 }))[0];

        row["embedded"].Should().Be(0);
        row["pending"].Should().Be(2);
        row["failed"].Should().Be(0);
        row["total"].Should().Be(2);
    }

    [Fact]
    public async Task HeadCoverage_CountsHeadsNoVectors()
    {
        await UpsertRule("git:repo");     // 2-segment head
        await UpsertRule("git:repo:file"); // nested leaf (not a head)
        await UpsertRule("normal");

        var row = (await _sut.QueryAsync(HeadCoverageQuery, null))[0];

        row["totalHeads"].Should().Be(1);
        row["withVectors"].Should().Be(0);
    }

    // ── Embedding-gated reads → empty in memory mode ────────────────────────────

    [Theory]
    [InlineData("CALL db.index.vector.queryNodes($index, $topK, $vector) YIELD node, score " +
        "WHERE score > $threshold RETURN node.parentId AS id, max(score) AS score")]
    [InlineData("MATCH (r:Rule) OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk) WITH r, count(c) AS chunks " +
        "WHERE (r.bodyHash IS NULL OR chunks = 0) AND coalesce(r.embedAttempts, 0) < $maxAttempts " +
        "RETURN r.id AS id, r.body AS body, r.chunkStyle AS chunkStyle")]
    [InlineData("MATCH (r:Rule {id: $ruleId}) OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk) " +
        "RETURN r.bodyHash AS hash, count(c) AS chunks")]
    [InlineData("MATCH (c:RuleChunk) WHERE c.parentId STARTS WITH $prefix RETURN c.embedding AS emb")]
    public async Task EmbeddingGatedReads_ReturnEmpty(string query)
    {
        await UpsertRule("a");
        var rows = await _sut.QueryAsync(query, new { index = "chunk_embeddings", topK = 5, vector = Array.Empty<double>(), threshold = 0.5, maxAttempts = 5, ruleId = "a", prefix = "git:repo:" });
        rows.Should().BeEmpty();
    }

    // ── Embedding-gated writes → no-op (never throw) ────────────────────────────

    [Theory]
    [InlineData("MATCH (r:Rule {id: $id}) SET r.bodyHash = $hash, r.embedAttempts = null WITH r " +
        "UNWIND $chunks AS ch CREATE (r)-[:HAS_CHUNK]->(:RuleChunk {parentId: $id, ord: ch.ord})")]
    [InlineData("CREATE VECTOR INDEX chunk_embeddings IF NOT EXISTS FOR (c:RuleChunk) ON (c.embedding)")]
    [InlineData("UNWIND $vectors AS v CREATE (:HeadVector {headId: $id, ord: v.ord, embedding: v.emb})")]
    [InlineData("MATCH (h:Rule {id: $id}) SET h.headVectorDirty = true")]
    [InlineData("CREATE CONSTRAINT entity_owner_name_unique IF NOT EXISTS FOR (e:Entity) " +
        "REQUIRE (e.ownerId, e.normalizedName) IS UNIQUE")]
    public async Task EmbeddingGatedWrites_DoNotThrow(string query)
    {
        var act = () => _sut.ExecuteAsync(query,
            new { id = "a", hash = "h", chunks = Array.Empty<object>(), vectors = Array.Empty<object>() });
        await act.Should().NotThrowAsync();
    }
}
