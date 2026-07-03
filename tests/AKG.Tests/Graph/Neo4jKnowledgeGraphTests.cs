using Edda.AKG.Background;
using Edda.AKG.Chunking;
using Edda.AKG.Context;
using Edda.AKG.Embeddings;
using Edda.AKG.Graph;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Neo4j.Driver;
using FluentAssertions;

namespace Edda.AKG.Tests.Graph;

public class Neo4jKnowledgeGraphTests
{
    private readonly FakeCypherExecutor _cypher = new();

    private static INode CreateRuleNode(
        string id,
        string? ownerId = null,
        string body = "rule body",
        string priority = "Medium",
        string domain = "general")
    {
        var node = new Mock<INode>();
        var props = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["body"] = body,
            ["priority"] = priority,
            ["domain"] = domain,
            ["type"] = "Rule",
            ["tags"] = new List<object>(),
            ["ownerId"] = ownerId,
        };
        node.SetupGet(n => n.Properties).Returns(props);
        node.Setup(n => n[It.IsAny<string>()]).Returns<string>(k => props[k]!);
        return node.Object;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> EmptyRows()
        => Array.Empty<IReadOnlyDictionary<string, object?>>();

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> RowsWithNode(string key, INode node)
        => new[]
        {
            (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { [key] = node }
        };

    private ContextCompiler CreateContextCompiler()
    {
        var embeddingService = new Mock<IEmbeddingService>();
        embeddingService.SetupGet(e => e.IsAvailable).Returns(false);

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<Microsoft.Extensions.Logging.ILogger>().Object);

        var logger = new Mock<ILogger<ContextCompiler>>();

        return new ContextCompiler(
            _cypher,
            embeddingService.Object,
            logger.Object,
            loggerFactory.Object,
            TimeProvider.System);
    }

    private RuleLoader CreateRuleLoader()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
        var logger = new Mock<ILogger<RuleLoader>>();
        return new RuleLoader(fs.Object, _cypher, TimeProvider.System, logger.Object);
    }

    private WorldKnowledgeSeeder CreateWorldKnowledgeSeeder()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
        var logger = new Mock<ILogger<WorldKnowledgeSeeder>>();
        return new WorldKnowledgeSeeder(fs.Object, _cypher, logger.Object);
    }

    private Neo4jEmbeddingCache CreateEmbeddingCache()
    {
        var embedding = new Mock<IEmbeddingService>();
        embedding.SetupGet(e => e.IsAvailable).Returns(false);
        var logger = new Mock<ILogger<Neo4jEmbeddingCache>>();
        return new Neo4jEmbeddingCache(
            _cypher, embedding.Object, new AdaptiveDocumentChunker(), () => new ChunkingOptions(), logger.Object);
    }

    private static IHeadVectorStore HeadVectorStore()
    {
        var mock = new Mock<IHeadVectorStore>();
        mock.Setup(s => s.GetCoverageAsync(It.IsAny<CancellationToken>())).ReturnsAsync((0, 0));
        return mock.Object;
    }

    private Neo4jKnowledgeGraph CreateGraph()
    {
        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.GetFullPath(It.IsAny<string>())).Returns("/knowledge");
        var logger = new Mock<ILogger<Neo4jKnowledgeGraph>>();

        return new Neo4jKnowledgeGraph(
            _cypher,
            CreateContextCompiler(),
            CreateRuleLoader(),
            CreateWorldKnowledgeSeeder(),
            CreateEmbeddingCache(),
            HeadVectorStore(),
            fs.Object,
            TimeProvider.System,
            new ChannelBackgroundWorkQueue(),
            logger.Object);
    }

    [Fact]
    public async Task InvalidateSupersededRulesAsync_IssuesInvalidationWrite()
    {
        var graph = CreateGraph();

        await graph.InvalidateSupersededRulesAsync();

        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("validUntil") && q.Contains("supersedes") && q.Contains("invalidatedBy"));
    }

    [Fact]
    public async Task InvalidateSupersededRulesAsync_ClosesEdgesOfInvalidatedRule()
    {
        // C9: a superseded fact's relationships end with it — the invalidation write also closes the
        // rule's open edges, keeping only the incoming SUPERSEDES edge that documents the supersession.
        var graph = CreateGraph();

        await graph.InvalidateSupersededRulesAsync();

        var query = _cypher.ExecutedWriteQueries.Single(q => q.Contains("UNWIND newer.supersedes"));
        query.Should().Contain("OPTIONAL MATCH (older)-[e]-(other:Rule)");
        query.Should().Contain("SET e.validUntil = $now");
        query.Should().Contain("type(e) = 'SUPERSEDES' AND endNode(e) = older");
    }

    [Fact]
    public async Task ResetAndRebuildEmbeddingsAsync_ClearsEmbeddingsBeforeRebuild()
    {
        var graph = CreateGraph();

        await graph.ResetAndRebuildEmbeddingsAsync();

        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("HAS_CHUNK") && q.Contains("DELETE"));
        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("REMOVE r.bodyHash"));
    }

    [Fact]
    public void BeginBulkIngestion_ReturnsScope_AndDisposeIsIdempotent()
    {
        var graph = CreateGraph();

        var scope = graph.BeginBulkIngestion();

        scope.Should().NotBeNull();
        scope.Dispose();
        var disposeAgain = () => scope.Dispose();
        disposeAgain.Should().NotThrow();
    }

    [Fact]
    public async Task UpsertRuleAsync_DuringBulkIngestion_ClearsBodyHashForDeferredRebuild()
    {
        var graph = CreateGraph();

        using (graph.BeginBulkIngestion())
        {
            await graph.UpsertRuleAsync(new KnowledgeRule
            {
                Id = "r1", Domain = "d", Type = "Rule", Priority = RulePriority.Medium, Body = "b",
            });
        }

        // Inline embedding is skipped; the rule's body hash is cleared so the deferred rebuild re-embeds it.
        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("REMOVE r.bodyHash"));
    }

    [Fact]
    public async Task GetRuleHeadsAsync_ExcludesFileLeaves_AndMapsHeads()
    {
        var head = CreateRuleNode("git:my-repo", domain: "git-knowledge");
        _cypher.DefaultResult = RowsWithNode("r", head);
        var graph = CreateGraph();

        var heads = await graph.GetRuleHeadsAsync();

        heads.Should().ContainSingle().Which.Id.Should().Be("git:my-repo");
        _cypher.ExecutedQueries.Should().Contain(q =>
            q.Contains("STARTS WITH 'git:'") && q.Contains("split(r.id"));
    }

    [Fact]
    public async Task DeleteSubtreeAsync_RepoHead_DeletesSubtreeAndReturnsCount()
    {
        var repo = CreateRuleNode("git:my-repo", domain: "git-knowledge");
        _cypher.AddQueryHandler(q => q.Contains("count(r)")
            ? new IReadOnlyDictionary<string, object?>[] { new Dictionary<string, object?> { ["n"] = 3 } }
            : RowsWithNode("r", repo));   // GetRuleAsync ownership lookup
        var graph = CreateGraph();

        var deleted = await graph.DeleteSubtreeAsync("git:my-repo", "local", isAdmin: true);

        deleted.Should().Be(3);
        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("DETACH DELETE") && q.Contains("HAS_CHUNK"));
    }

    [Fact]
    public async Task DeleteSubtreeAsync_NonAdminForeignNode_Throws()
    {
        var global = CreateRuleNode("git:my-repo", ownerId: null);
        _cypher.DefaultResult = RowsWithNode("r", global);
        var graph = CreateGraph();

        var act = async () => await graph.DeleteSubtreeAsync("git:my-repo", "user-1", isAdmin: false);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetRuleAsync_RuleFound_ReturnsMappedRule()
    {
        var node = CreateRuleNode("rule-001", domain: "csharp", priority: "High");
        _cypher.DefaultResult = RowsWithNode("r", node);

        var graph = CreateGraph();

        var result = await graph.GetRuleAsync("rule-001");

        result.Should().NotBeNull();
        result!.Id.Should().Be("rule-001");
        result.Domain.Should().Be("csharp");
        result.Priority.Should().Be(RulePriority.High);
    }

    [Fact]
    public async Task GetRuleAsync_NotFound_ReturnsNull()
    {
        _cypher.DefaultResult = EmptyRows();
        var graph = CreateGraph();

        var result = await graph.GetRuleAsync("nonexistent-rule");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRulesAsync_WithDomainFilter_IncludesDomainInQuery()
    {
        _cypher.DefaultResult = EmptyRows();
        var graph = CreateGraph();

        await graph.GetRulesAsync(domain: "csharp");

        _cypher.ExecutedQueries.Should().Contain(q => q.Contains("r.domain"));
    }

    [Fact]
    public async Task DeleteRuleAsync_NotOwnerAndNotAdmin_ThrowsUnauthorizedAccess()
    {
        var node = CreateRuleNode("rule-owned", ownerId: "other-user");
        _cypher.DefaultResult = RowsWithNode("r", node);

        var graph = CreateGraph();

        var act = async () => await graph.DeleteRuleAsync("rule-owned", userId: "current-user", isAdmin: false);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task DeleteRuleAsync_SoftDeletes_SetsMarkersInsteadOfDetachDelete()
    {
        // E10: deletion marks the rule (deletedAt/deletedBy + coalesced validUntil) instead of
        // removing it, so it can be restored from the recycle bin.
        var node = CreateRuleNode("rule-soft", ownerId: "u1");
        _cypher.DefaultResult = RowsWithNode("r", node);
        var graph = CreateGraph();

        await graph.DeleteRuleAsync("rule-soft", userId: "u1");

        var deleteQuery = _cypher.ExecutedWriteQueries.Single(q => q.Contains("r.deletedAt"));
        deleteQuery.Should().Contain("SET r.deletedAt = $now");
        deleteQuery.Should().Contain("r.validUntil = coalesce(r.validUntil, $now)");
        _cypher.ExecutedWriteQueries.Should().NotContain(q => q.Contains("DETACH DELETE"));
    }

    [Fact]
    public async Task GetRuleAsync_Query_ExcludesSoftDeleted()
    {
        var graph = CreateGraph();

        await graph.GetRuleAsync("any-rule", "u1");

        _cypher.ExecutedQueries.Should().Contain(q =>
            q.Contains("{id: $ruleId}") && q.Contains("r.deletedAt IS NULL"));
    }

    [Fact]
    public async Task UpsertRuleAsync_EmbeddingAvailable_EmbedsSingleRule()
    {
        var embeddingService = new Mock<IEmbeddingService>();
        embeddingService.SetupGet(e => e.IsAvailable).Returns(true);
        embeddingService.Setup(e => e.EmbedBatchAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([[0.1f, 0.2f, 0.3f]]);

        var embeddingLogger = new Mock<ILogger<Neo4jEmbeddingCache>>();
        var embeddingCache = new Neo4jEmbeddingCache(
            _cypher, embeddingService.Object, new AdaptiveDocumentChunker(), () => new ChunkingOptions(),
            embeddingLogger.Object);

        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.GetFullPath(It.IsAny<string>())).Returns("/knowledge");
        var logger = new Mock<ILogger<Neo4jKnowledgeGraph>>();

        var graph = new Neo4jKnowledgeGraph(
            _cypher,
            CreateContextCompiler(),
            CreateRuleLoader(),
            CreateWorldKnowledgeSeeder(),
            embeddingCache,
            HeadVectorStore(),
            fs.Object,
            TimeProvider.System,
            new ChannelBackgroundWorkQueue(),
            logger.Object);

        var rule = new KnowledgeRule
        {
            Id       = "test-rule",
            Domain   = "csharp",
            Type     = "Rule",
            Priority = RulePriority.Medium,
            Body     = "Always use async/await.",
        };

        await graph.UpsertRuleAsync(rule);

        // A chunk-creation write must have been issued (hidden :RuleChunk children).
        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains(":RuleChunk"));
    }

    [Fact]
    public async Task UpsertRuleAsync_EmbeddingUnavailable_SkipsEmbedding()
    {
        // CreateEmbeddingCache() uses IsAvailable = false
        var graph = CreateGraph();

        var rule = new KnowledgeRule
        {
            Id       = "test-rule-2",
            Domain   = "csharp",
            Type     = "Rule",
            Priority = RulePriority.Medium,
            Body     = "Prefer records over classes for DTOs.",
        };

        await graph.UpsertRuleAsync(rule);

        _cypher.ExecutedWriteQueries.Should().NotContain(q => q.Contains(":RuleChunk"));
    }

    [Fact]
    public async Task UpsertRuleAsync_WithRelatedRelations_WritesRelatedPropertyAndEdge()
    {
        var graph = CreateGraph();
        var rule = new KnowledgeRule
        {
            Id       = "rule-with-related",
            Domain   = "docs",
            Type     = "WorldKnowledge",
            Priority = RulePriority.Medium,
            Body     = "Body.",
            RelatesTo = new RuleRelations { Related = ["other-rule"] },
        };

        await graph.UpsertRuleAsync(rule);

        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("r.related"));
        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("RELATED"));
    }

    [Fact]
    public async Task UpsertRuleAsync_WithManyTargetsInRelation_IssuesSingleBatchedEdgeQueryPerRelationType()
    {
        var graph = CreateGraph();
        var targets = Enumerable.Range(0, 25).Select(i => $"target-{i}").ToArray();
        var rule = new KnowledgeRule
        {
            Id        = "batch-source",
            Domain    = "docs",
            Type      = "WorldKnowledge",
            Priority  = RulePriority.Medium,
            Body      = "Body.",
            RelatesTo = new RuleRelations { Related = targets },
        };

        await graph.UpsertRuleAsync(rule);

        // Exactly one write query MERGEs the RELATED edges, regardless of the number of targets
        // (previously this was one MERGE per target — the N+1 round-trips that D1 removes).
        _cypher.ExecutedWriteQueries.Count(q => q.Contains("MERGE (s)-[e:RELATED]")).Should().Be(1);
        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("MERGE (s)-[e:RELATED]") && q.Contains("UNWIND $targetIds AS targetId"));
    }

    [Fact]
    public async Task UpsertRuleAsync_EdgeUpsert_UsesTemporalReplaceQuery()
    {
        // C9: dropped edges are closed (validUntil) instead of deleted; created edges get a
        // first-seen validFrom; re-declared edges are re-opened.
        var graph = CreateGraph();
        var rule = new KnowledgeRule
        {
            Id        = "temporal-source",
            Domain    = "docs",
            Type      = "Rule",
            Priority  = RulePriority.Medium,
            Body      = "Body.",
            RelatesTo = new RuleRelations { Related = ["other"] },
        };

        await graph.UpsertRuleAsync(rule);

        var edgeQuery = _cypher.ExecutedWriteQueries.Single(q => q.Contains("MERGE (s)-[e:RELATED]"));
        edgeQuery.Should().Contain("SET stale.validUntil = $now");
        edgeQuery.Should().Contain("ON CREATE SET e.validFrom = $now");
        edgeQuery.Should().Contain("SET e.validUntil = null");
        edgeQuery.Should().NotContain("DELETE e");
    }

    [Fact]
    public async Task UpsertRuleAsync_WithConcepts_PersistsConceptsProperty()
    {
        // B5: the rule's trigger concepts must survive the round-trip — previously the SET clause
        // dropped them, leaving the scorer's concept branch dead on graph-loaded rules.
        var graph = CreateGraph();
        var rule = new KnowledgeRule
        {
            Id = "concept-rule",
            Domain = "docs",
            Type = "Rule",
            Priority = RulePriority.Medium,
            Body = "Body.",
            WhenRelevant = new WhenRelevant { DetectedConcepts = ["password", "secret"] },
        };

        await graph.UpsertRuleAsync(rule);

        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("MERGE (r:Rule {id: $id})") && q.Contains("r.concepts = $concepts"));
    }

    [Fact]
    public async Task UpsertRuleAsync_LlmValidator_PersistsTypeAndPrompt()
    {
        // F16: validatorType/validatorPrompt survive the round-trip like the other validator fields.
        var graph = CreateGraph();
        var rule = new KnowledgeRule
        {
            Id = "llm-rule",
            Domain = "coding",
            Type = "Rule",
            Priority = RulePriority.Medium,
            Body = "Body.",
            ValidatorType = "llm",
            ValidatorPrompt = "Error messages must be actionable.",
        };

        await graph.UpsertRuleAsync(rule);

        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("MERGE (r:Rule {id: $id})")
            && q.Contains("r.validatorType = $validatorType")
            && q.Contains("r.validatorPrompt = $validatorPrompt"));
    }

    [Fact]
    public async Task GetRuleAsync_NodeWithLlmValidator_MapsTypeAndPrompt()
    {
        var node = new Mock<INode>();
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object?>
        {
            ["id"] = "llm-rule",
            ["body"] = "b",
            ["priority"] = "Medium",
            ["domain"] = "coding",
            ["type"] = "Rule",
            ["tags"] = new List<object>(),
            ["validatorType"] = "llm",
            ["validatorPrompt"] = "Error messages must be actionable.",
        });
        _cypher.DefaultResult = RowsWithNode("r", node.Object);
        var graph = CreateGraph();

        var rule = await graph.GetRuleAsync("llm-rule", "u1");

        rule.Should().NotBeNull();
        rule!.ValidatorType.Should().Be("llm");
        rule.ValidatorPrompt.Should().Be("Error messages must be actionable.");
    }

    [Fact]
    public async Task GetRuleAsync_NodeWithConcepts_MapsWhenRelevant()
    {
        var node = new Mock<INode>();
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object?>
        {
            ["id"] = "concept-rule",
            ["body"] = "b",
            ["priority"] = "Medium",
            ["domain"] = "docs",
            ["type"] = "Rule",
            ["tags"] = new List<object>(),
            ["concepts"] = new List<object> { "password", "secret" },
        });
        _cypher.DefaultResult = RowsWithNode("r", node.Object);
        var graph = CreateGraph();

        var rule = await graph.GetRuleAsync("concept-rule", "u1");

        rule.Should().NotBeNull();
        rule!.WhenRelevant.Should().NotBeNull();
        rule.WhenRelevant!.DetectedConcepts.Should().BeEquivalentTo(["password", "secret"]);
    }

    [Fact]
    public async Task UpsertRuleAsync_EmptyRelationList_StillClosesStaleEdges()
    {
        // C9: the old guard skipped the edge query for empty lists, leaving dropped relations
        // dangling. The temporal replace runs for every relation type and closes leftovers.
        var graph = CreateGraph();
        var rule = new KnowledgeRule
        {
            Id       = "no-relations",
            Domain   = "docs",
            Type     = "Rule",
            Priority = RulePriority.Medium,
            Body     = "Body.",
        };

        await graph.UpsertRuleAsync(rule);

        _cypher.ExecutedWriteQueries.Count(q => q.Contains("SET stale.validUntil = $now")).Should().Be(6,
            "each of the six relation types closes its stale edges even when the declared list is empty");
    }

    [Fact]
    public async Task GetRuleAsync_NodeWithRelated_MapsRelatedRelation()
    {
        var node = new Mock<INode>();
        var props = new Dictionary<string, object?>
        {
            ["id"] = "rule-r",
            ["body"] = "b",
            ["priority"] = "Medium",
            ["domain"] = "docs",
            ["type"] = "WorldKnowledge",
            ["tags"] = new List<object>(),
            ["related"] = new List<object> { "rule-x", "rule-y" },
        };
        node.SetupGet(n => n.Properties).Returns(props);
        node.Setup(n => n[It.IsAny<string>()]).Returns<string>(k => props[k]!);
        _cypher.DefaultResult = RowsWithNode("r", node.Object);

        var graph = CreateGraph();

        var result = await graph.GetRuleAsync("rule-r");

        result.Should().NotBeNull();
        result!.RelatesTo.Should().NotBeNull();
        result.RelatesTo!.Related.Should().BeEquivalentTo("rule-x", "rule-y");
    }

}
