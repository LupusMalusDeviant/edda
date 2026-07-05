using Edda.AKG.Context;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Moq;
using Neo4j.Driver;

namespace Edda.AKG.Tests.Context;

public class ContextCompilerTests
{
    private readonly FakeCypherExecutor _cypher = new();
    private readonly Mock<IEmbeddingService> _embeddings = new();
    private readonly Mock<ILogger<ContextCompiler>> _logger = new();
    private readonly Mock<ILoggerFactory> _loggerFactory = new();

    public ContextCompilerTests()
    {
        _embeddings.SetupGet(e => e.IsAvailable).Returns(false);
        _loggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<Microsoft.Extensions.Logging.ILogger>());
    }

    private ContextCompiler CreateCompiler(RetrievalOptions? options = null, IEddaTelemetry? telemetry = null) => new(
        _cypher,
        _embeddings.Object,
        _logger.Object,
        _loggerFactory.Object,
        TimeProvider.System,
        options: options,
        telemetry: telemetry);

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> EmptyRows()
        => Array.Empty<IReadOnlyDictionary<string, object?>>();

    private static INode CreateRuleNode(
        string id,
        string body = "rule body",
        string priority = "Medium",
        string domain = "general",
        List<object>? conflictsWith = null)
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
        };
        if (conflictsWith is not null)
            props["conflictsWith"] = conflictsWith;

        node.SetupGet(n => n.Properties).Returns(props);
        return node.Object;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> RowsWithNode(string key, INode node)
        => [new Dictionary<string, object?> { [key] = node }];

    [Fact]
    public async Task CompileAsync_RecordsTelemetrySpanAndDuration()
    {
        _cypher.DefaultResult = EmptyRows();
        var telemetry = new Mock<IEddaTelemetry>();
        var compiler = CreateCompiler(telemetry: telemetry.Object);

        await compiler.CompileAsync(new TaskContext { Task = "t", UserId = "u" }, CancellationToken.None);

        telemetry.Verify(t => t.StartActivity(TelemetryOperations.ContextCompilation), Times.Once);
        telemetry.Verify(
            t => t.RecordDuration(TelemetryOperations.ContextCompilation, It.IsAny<double>(), true), Times.Once);
    }

    [Fact]
    public async Task CompileAsync_EmptyGraph_ReturnsEmptyActiveRulesAndNoConflicts()
    {
        _cypher.DefaultResult = EmptyRows();
        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "test task", UserId = "user-1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        result.ActiveRules.Should().BeEmpty();
        result.Conflicts.Should().BeEmpty();
        result.Exceptions.Should().BeEmpty();
    }

    // ── B5: co-occurrence query expansion ────────────────────────────────────────

    private static INode RuleNodeWithTerms(string id, List<object> tags, List<object> concepts)
    {
        var node = new Mock<INode>();
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["body"] = "rule body",
            ["priority"] = "Medium",
            ["domain"] = "general",
            ["type"] = "Rule",
            ["tags"] = tags,
            ["concepts"] = concepts,
        });
        return node.Object;
    }

    private void ArrangeExpansionRules()
    {
        // seed matches the query "kafka" directly and contributes "broker" as a co-occurrence term.
        // related is reachable only via that expanded term (score ≈ 2·ln(1+1.5) with weight 0.5).
        // competitor matches the weak task token "setup" directly (score = 2·ln(1+1)) — so the
        // ranking of related vs. competitor flips depending on whether expansion is active.
        var seed = RuleNodeWithTerms("seed", tags: ["kafka"], concepts: ["broker"]);
        var related = RuleNodeWithTerms("related", tags: [], concepts: ["broker"]);
        var competitor = RuleNodeWithTerms("competitor", tags: ["setup"], concepts: []);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["r"] = seed },
            new Dictionary<string, object?> { ["r"] = related },
            new Dictionary<string, object?> { ["r"] = competitor },
        ];
        _cypher.AddQueryHandler(q => q.Contains("NOT r.domain STARTS WITH") ? rows : _cypher.DefaultResult);
    }

    private static int RankOf(ContextResult result, string id)
        => result.ActiveRules.Select(r => r.Id).ToList().IndexOf(id);

    [Fact]
    public async Task CompileAsync_ExpansionEnabled_RanksCoOccurrenceRuleAboveWeakDirectMatch()
    {
        _cypher.DefaultResult = EmptyRows();
        ArrangeExpansionRules();
        var compiler = CreateCompiler(new RetrievalOptions { QueryExpansionTerms = 3 });
        var context = new TaskContext { Task = "kafka setup", UserId = "user-1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        RankOf(result, "related").Should().BeGreaterThanOrEqualTo(0);
        RankOf(result, "related").Should().BeLessThan(RankOf(result, "competitor"),
            because: "the expanded concept match (+3·0.5) outweighs the single weak task-token match");
    }

    [Fact]
    public async Task CompileAsync_ExpansionDisabled_RanksCoOccurrenceRuleBelowDirectMatch()
    {
        _cypher.DefaultResult = EmptyRows();
        ArrangeExpansionRules();
        var compiler = CreateCompiler(); // defaults: QueryExpansionTerms = 0
        var context = new TaskContext { Task = "kafka setup", UserId = "user-1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        RankOf(result, "competitor").Should().BeGreaterThanOrEqualTo(0);
        RankOf(result, "competitor").Should().BeLessThan(RankOf(result, "related"),
            because: "without expansion the related rule has no keyword overlap at all");
    }

    // ── F49: entity-layer fusion ────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_WithEntityStore_AppendsRelatedEntitiesSection()
    {
        _cypher.DefaultResult = EmptyRows();

        var entityStore = new Mock<IEntityStore>();
        entityStore
            .Setup(s => s.FindEntitiesAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GraphEntity { Name = "Neo4j", Type = "technology" }]);
        entityStore
            .Setup(s => s.GetRelatedAsync(
                "Neo4j", It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GraphEntity { Name = "Cypher", Type = "technology" }]);

        var compiler = new ContextCompiler(
            _cypher, _embeddings.Object, _logger.Object,
            _loggerFactory.Object, TimeProvider.System, entityStore: entityStore.Object);
        var context = new TaskContext { Task = "graph db", Concepts = ["neo4j"], UserId = "user-1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        result.FormattedContext.Should().Contain("Verwandte Entitäten");
        result.FormattedContext.Should().Contain("Neo4j");
        result.FormattedContext.Should().Contain("Cypher");
    }

    [Fact]
    public async Task CompileAsync_NoEntityStore_NoRelatedEntitiesSection()
    {
        _cypher.DefaultResult = EmptyRows();
        var compiler = CreateCompiler(); // no entity store
        var context = new TaskContext { Task = "x", Concepts = ["neo4j"], UserId = "user-1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        result.FormattedContext.Should().NotContain("Verwandte Entitäten");
    }

    [Fact]
    public async Task CompileAsync_EntityStoreNoMatches_NoRelatedEntitiesSection()
    {
        _cypher.DefaultResult = EmptyRows();
        var entityStore = new Mock<IEntityStore>();
        entityStore
            .Setup(s => s.FindEntitiesAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var compiler = new ContextCompiler(
            _cypher, _embeddings.Object, _logger.Object,
            _loggerFactory.Object, TimeProvider.System, entityStore: entityStore.Object);
        var context = new TaskContext { Task = "x", Concepts = ["neo4j"], UserId = "user-1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        result.FormattedContext.Should().NotContain("Verwandte Entitäten");
    }

    [Fact]
    public async Task CompileAsync_WithRulesInGraph_IncludesThemInActiveRules()
    {
        var ruleNode = CreateRuleNode("async-rule", body: "Use async/await consistently");
        _cypher.AddQueryHandler(q =>
            q.Contains("MATCH (r:Rule)") && q.Contains("ownerId")
                ? RowsWithNode("r", ruleNode)
                : EmptyRows());

        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "fix async issue", UserId = "u1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        result.ActiveRules.Should().Contain(r => r.Id == "async-rule");
    }

    [Fact]
    public async Task CompileAsync_WithRules_FormattedContextContainsActiveRulesSection()
    {
        var ruleNode = CreateRuleNode("rule-001", body: "Follow naming conventions");
        _cypher.AddQueryHandler(q =>
            q.Contains("MATCH (r:Rule)") && q.Contains("ownerId")
                ? RowsWithNode("r", ruleNode)
                : EmptyRows());

        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "refactor code", UserId = "u1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        result.FormattedContext.Should().Contain("## Active Knowledge Rules");
        result.FormattedContext.Should().Contain("rule-001");
    }

    [Fact]
    public async Task CompileAsync_ConflictingRulesBothActive_DetectsConflict()
    {
        var nodeA = CreateRuleNode("rule-a", body: "rule a", conflictsWith: ["rule-b"]);
        var nodeB = CreateRuleNode("rule-b", body: "rule b");

        _cypher.AddQueryHandler(q =>
            q.Contains("MATCH (r:Rule)") && q.Contains("ownerId")
                ?
                [
                    new Dictionary<string, object?> { ["r"] = nodeA },
                    new Dictionary<string, object?> { ["r"] = nodeB },
                ]
                : EmptyRows());

        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "any task", UserId = "u1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].RuleIdA.Should().Be("rule-a");
        result.Conflicts[0].RuleIdB.Should().Be("rule-b");
    }

    [Fact]
    public async Task CompileAsync_SemanticBoostingSkipped_WhenEmbeddingServiceUnavailable()
    {
        // Embedding service unavailable — no embedding calls should be made
        _embeddings.SetupGet(e => e.IsAvailable).Returns(false);
        _cypher.DefaultResult = EmptyRows();

        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "task", UserId = "u1" };

        await compiler.CompileAsync(context, CancellationToken.None);

        _embeddings.Verify(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompileAsync_WebQuery_ToolboxFilterPassedToCypher()
    {
        string? capturedQuery = null;
        _cypher.AddQueryHandler(q =>
        {
            // The rule-load query carries the toolbox filter. The multi-hop expansion query also
            // starts with "MATCH (r:Rule)", so match specifically on the toolbox parameter.
            if (q.Contains("$toolboxes"))
                capturedQuery = q;
            return EmptyRows();
        });

        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "search the web for news", UserId = "u1" };

        await compiler.CompileAsync(context, CancellationToken.None);

        capturedQuery.Should().NotBeNull();
        capturedQuery.Should().Contain("tools.");
        capturedQuery.Should().Contain("$toolboxes");
    }

    [Fact]
    public async Task CompileAsync_NonToolRules_AlwaysLoaded()
    {
        var generalRule = CreateRuleNode("coding-rule", body: "Use async", domain: "coding");
        _cypher.AddQueryHandler(q =>
            q.Contains("MATCH (r:Rule)")
                ? RowsWithNode("r", generalRule)
                : EmptyRows());

        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "fix async issue", UserId = "u1" };

        var result = await compiler.CompileAsync(context, CancellationToken.None);

        // Non-tool domain rules are always included regardless of toolbox filtering
        result.ActiveRules.Should().Contain(r => r.Id == "coding-rule");
    }

    [Fact]
    public async Task CompileAsync_LoadQuery_FiltersByValidUntil()
    {
        string? capturedQuery = null;
        _cypher.AddQueryHandler(q =>
        {
            if (q.Contains("$toolboxes"))
                capturedQuery = q;
            return EmptyRows();
        });

        var compiler = CreateCompiler();
        var context = new TaskContext { Task = "any task", UserId = "u1" };

        await compiler.CompileAsync(context, CancellationToken.None);

        capturedQuery.Should().NotBeNull();
        capturedQuery.Should().Contain("validUntil",
            because: "expired rules must be excluded from context compilation");
    }

    // ── ADR-0009: hierarchical stage-1 head pre-pruning ──────────────────────

    [Fact]
    public async Task CompileAsync_WithHeadVectorStore_QueriesStage1AndEmbedsQueryOnce()
    {
        _cypher.DefaultResult = EmptyRows();
        _embeddings.SetupGet(e => e.IsAvailable).Returns(true);
        _embeddings.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new[] { 1f, 0f, 0f });
        var heads = new Mock<IHeadVectorStore>();
        heads.Setup(h => h.FindTopHeadsAsync(
                  It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { new HeadMatch("git:edda", 0.8) });

        var compiler = new ContextCompiler(
            _cypher, _embeddings.Object, _logger.Object, _loggerFactory.Object, TimeProvider.System,
            headVectorStore: heads.Object);

        await compiler.CompileAsync(new TaskContext { Task = "scraper", UserId = "u1" }, CancellationToken.None);

        heads.Verify(h => h.FindTopHeadsAsync(
            It.IsAny<float[]>(), 3, 0.4, "u1", It.IsAny<CancellationToken>()), Times.Once);
        _embeddings.Verify(e => e.EmbedAsync("scraper", It.IsAny<CancellationToken>()), Times.Once,
            "the query is embedded once (stage 0) and shared with the semantic phase");
    }

    [Fact]
    public async Task CompileAsync_HeadStoreFindsNoHeads_FallsBackToFullScan()
    {
        _cypher.DefaultResult = EmptyRows();
        _embeddings.SetupGet(e => e.IsAvailable).Returns(true);
        _embeddings.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new[] { 1f, 0f, 0f });
        var heads = new Mock<IHeadVectorStore>();
        heads.Setup(h => h.FindTopHeadsAsync(
                  It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Array.Empty<HeadMatch>());

        var compiler = new ContextCompiler(
            _cypher, _embeddings.Object, _logger.Object, _loggerFactory.Object, TimeProvider.System,
            headVectorStore: heads.Object);

        var result = await compiler.CompileAsync(new TaskContext { Task = "x", UserId = "u1" }, CancellationToken.None);

        result.ActiveRules.Should().BeEmpty(because: "no head over threshold → full scan over an empty graph");
    }
}
