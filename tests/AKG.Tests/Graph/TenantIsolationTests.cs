using Edda.AKG.Background;
using Edda.AKG.Chunking;
using Edda.AKG.Context;
using Edda.AKG.Embeddings;
using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Graph;

/// <summary>
/// C1 end-to-end isolation tests (definition of done): two graph instances with different ambient
/// tenants share one in-memory store — rules written under tenant A must be invisible under B,
/// the ambient context stamps the tenant (anti-spoofing), and legacy nodes without a tenant
/// property stay visible in the default tenant.
/// </summary>
public sealed class TenantIsolationTests
{
    private readonly ICypherExecutor _executor = new InMemoryCypherExecutor();

    private static IIdentityContext Identity(string tenant)
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");
        identity.SetupGet(i => i.TenantId).Returns(tenant);
        identity.SetupGet(i => i.IsAdmin).Returns(true);
        return identity.Object;
    }

    private Neo4jKnowledgeGraph CreateGraph(IIdentityContext identity)
    {
        var embeddings = new Mock<IEmbeddingService>();
        embeddings.SetupGet(e => e.IsAvailable).Returns(false);

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<Microsoft.Extensions.Logging.ILogger>());

        var compiler = new ContextCompiler(
            _executor,
            embeddings.Object,
            Mock.Of<ILogger<ContextCompiler>>(),
            loggerFactory.Object,
            TimeProvider.System,
            identity: identity);

        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
        fs.Setup(f => f.GetFullPath(It.IsAny<string>())).Returns("/knowledge");

        var headVectorStore = new Mock<IHeadVectorStore>();
        headVectorStore.Setup(s => s.GetCoverageAsync(It.IsAny<CancellationToken>())).ReturnsAsync((0, 0));

        return new Neo4jKnowledgeGraph(
            _executor,
            compiler,
            new RuleLoader(fs.Object, _executor, TimeProvider.System, Mock.Of<ILogger<RuleLoader>>()),
            new WorldKnowledgeSeeder(fs.Object, _executor, Mock.Of<ILogger<WorldKnowledgeSeeder>>()),
            new Neo4jEmbeddingCache(
                _executor, embeddings.Object, new AdaptiveDocumentChunker(), () => new ChunkingOptions(),
                Mock.Of<ILogger<Neo4jEmbeddingCache>>()),
            headVectorStore.Object,
            fs.Object,
            TimeProvider.System,
            new ChannelBackgroundWorkQueue(),
            Mock.Of<ILogger<Neo4jKnowledgeGraph>>(),
            identity);
    }

    private static KnowledgeRule Rule(string id, string? modelTenant = null) => new()
    {
        Id = id,
        Type = "Rule",
        Domain = "testing",
        Priority = RulePriority.Medium,
        Body = $"Body of {id}.",
        OwnerId = "local",
        TenantId = modelTenant ?? Tenants.DefaultTenantId,
    };

    [Fact]
    public async Task Upsert_UnderTenantA_NotVisibleUnderTenantB()
    {
        var graphA = CreateGraph(Identity("tenant-a"));
        var graphB = CreateGraph(Identity("tenant-b"));

        await graphA.UpsertRuleAsync(Rule("a-rule"));

        (await graphA.GetRuleAsync("a-rule", "local")).Should().NotBeNull("the owning tenant sees its rule");
        (await graphB.GetRuleAsync("a-rule", "local")).Should().BeNull("tenant B must not see tenant A's rule");
        (await graphB.GetRulesAsync(userId: "local")).Should().BeEmpty();
        (await graphA.GetRulesAsync(userId: "local")).Should().ContainSingle();
    }

    [Fact]
    public async Task Upsert_AmbientContextStampsTenant_ModelFieldCannotSpoof()
    {
        // The rule claims tenant "spoofed", but the ambient context is tenant-a — the context wins.
        var graphA = CreateGraph(Identity("tenant-a"));
        var graphSpoofed = CreateGraph(Identity("spoofed"));

        await graphA.UpsertRuleAsync(Rule("spoof-attempt", modelTenant: "spoofed"));

        (await graphSpoofed.GetRuleAsync("spoof-attempt", "local")).Should().BeNull(
            "the model field must not override the ambient tenant");
        (await graphA.GetRuleAsync("spoof-attempt", "local")).Should().NotBeNull();
    }

    [Fact]
    public async Task CompileContext_TenantB_DoesNotSeeTenantARules()
    {
        var graphA = CreateGraph(Identity("tenant-a"));
        var graphB = CreateGraph(Identity("tenant-b"));
        await graphA.UpsertRuleAsync(Rule("kafka-rule") with { Tags = ["kafka"] });

        var contextA = await graphA.CompileContextAsync(
            new TaskContext { Task = "kafka setup", UserId = "local" });
        var contextB = await graphB.CompileContextAsync(
            new TaskContext { Task = "kafka setup", UserId = "local" });

        contextA.ActiveRules.Select(r => r.Id).Should().Contain("kafka-rule");
        contextB.ActiveRules.Should().BeEmpty("context compilation is tenant-scoped");
    }

    [Fact]
    public async Task RecycleBin_TenantIsolated()
    {
        var graphA = CreateGraph(Identity("tenant-a"));
        var binA = new RuleRecycleBin(
            _executor, Mock.Of<IAuditLog>(), NullLogger<RuleRecycleBin>.Instance, Identity("tenant-a"));
        var binB = new RuleRecycleBin(
            _executor, Mock.Of<IAuditLog>(), NullLogger<RuleRecycleBin>.Instance, Identity("tenant-b"));

        await graphA.UpsertRuleAsync(Rule("doomed"));
        await graphA.DeleteRuleAsync("doomed", "local");

        (await binA.ListAsync("local")).Should().ContainSingle();
        (await binB.ListAsync("local")).Should().BeEmpty("the bin is tenant-scoped");
        (await binB.RestoreAsync("doomed", "local")).Should().BeFalse("a foreign tenant cannot even probe");
    }

    [Fact]
    public async Task LegacyRule_WithoutTenantProperty_VisibleInDefaultTenant()
    {
        // Simulate a pre-tenancy node: upsert with a null tenantId parameter → property stays null,
        // and coalesce(...) counts it as the default tenant.
        await _executor.ExecuteAsync(
            """
            MERGE (r:Rule {id: $id})
            SET r.type = $type,
                r.domain = $domain,
                r.priority = $priority,
                r.body = $body,
                r.tags = $tags,
                r.ownerId = $ownerId,
                r.tenantId = $tenantId,
                r.validFrom = coalesce(r.validFrom, $now)
            """,
            new
            {
                id = "legacy-rule",
                type = "Rule",
                domain = "testing",
                priority = "Medium",
                body = "Legacy.",
                tags = Array.Empty<string>(),
                ownerId = (string?)null,
                tenantId = (string?)null,
                now = "2026-01-01T00:00:00.0000000+00:00",
            });

        var defaultGraph = CreateGraph(Identity(Tenants.DefaultTenantId));
        var foreignGraph = CreateGraph(Identity("tenant-a"));

        (await defaultGraph.GetRuleAsync("legacy-rule", "local")).Should().NotBeNull(
            "legacy nodes belong to the default tenant");
        (await foreignGraph.GetRuleAsync("legacy-rule", "local")).Should().BeNull();
    }
}
