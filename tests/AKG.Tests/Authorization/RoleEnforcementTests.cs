using Edda.AKG.Authorization;
using Edda.AKG.Background;
using Edda.AKG.Chunking;
using Edda.AKG.Context;
using Edda.AKG.Embeddings;
using Edda.AKG.Graph;
using Edda.AKG.Rules;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Authorization;

/// <summary>
/// C2 enforcement integration (definition of done): the central role matrix is actually applied at
/// the mutation points — graph delete, recycle-bin purge and the batch service — for identities
/// carrying a tenant role (IsAdmin = false, so nothing overrides the matrix).
/// </summary>
public sealed class RoleEnforcementTests
{
    private const string Now = "2026-07-03T12:00:00.0000000+00:00";

    private readonly ICypherExecutor _executor = new InMemoryCypherExecutor();

    private static IIdentityContext Identity(TenantRole role)
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");
        identity.SetupGet(i => i.TenantId).Returns(Tenants.DefaultTenantId);
        identity.SetupGet(i => i.IsAdmin).Returns(false);
        identity.SetupGet(i => i.Role).Returns(role);
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

    private static KnowledgeRule Rule(string id, string? owner = "local") => new()
    {
        Id = id,
        Type = "Rule",
        Domain = "testing",
        Priority = RulePriority.Medium,
        Body = $"Body of {id}.",
        OwnerId = owner,
    };

    /// <summary>Seeds a soft-deleted rule directly, bypassing the delete gate (foreign-rule setups).</summary>
    private async Task SeedDeleted(string id, string? ownerId)
    {
        await _executor.ExecuteAsync(
            """
            MERGE (r:Rule {id: $id})
            SET r.type = $type,
                r.domain = $domain,
                r.priority = $priority,
                r.body = $body,
                r.tags = $tags,
                r.ownerId = $ownerId,
                r.validFrom = coalesce(r.validFrom, $now)
            """,
            new
            {
                id,
                type = "Rule",
                domain = "testing",
                priority = "Medium",
                body = $"Body of {id}.",
                tags = Array.Empty<string>(),
                ownerId,
                now = Now,
            });
        await _executor.ExecuteAsync(
            """
            MATCH (r:Rule {id: $ruleId})
            SET r.deletedAt = $now,
                r.deletedBy = $userId,
                r.validUntil = coalesce(r.validUntil, $now)
            """,
            new { ruleId = id, userId = ownerId ?? "system", now = Now });
    }

    [Fact]
    public async Task DeleteRuleAsync_EditorOwnRule_SoftDeletes()
    {
        var graph = CreateGraph(Identity(TenantRole.Editor));
        await graph.UpsertRuleAsync(Rule("own-rule"));

        await graph.DeleteRuleAsync("own-rule", "local");

        (await graph.GetRuleAsync("own-rule", "local")).Should().BeNull("the rule is soft-deleted");
    }

    [Fact]
    public async Task DeleteRuleAsync_ViewerOwnRule_Throws()
    {
        var editorGraph = CreateGraph(Identity(TenantRole.Editor));
        await editorGraph.UpsertRuleAsync(Rule("own-rule"));
        var viewerGraph = CreateGraph(Identity(TenantRole.Viewer));

        var act = async () => await viewerGraph.DeleteRuleAsync("own-rule", "local");

        await act.Should().ThrowAsync<UnauthorizedAccessException>("Viewers must not mutate anything");
        (await editorGraph.GetRuleAsync("own-rule", "local")).Should().NotBeNull("the rule survived");
    }

    [Fact]
    public async Task RecycleBin_PurgeForeignRule_RequiresOwner()
    {
        await SeedDeleted("foreign", ownerId: "someone-else");
        var editorBin = new RuleRecycleBin(
            _executor, Mock.Of<IAuditLog>(), NullLogger<RuleRecycleBin>.Instance, Identity(TenantRole.Editor));
        var ownerBin = new RuleRecycleBin(
            _executor, Mock.Of<IAuditLog>(), NullLogger<RuleRecycleBin>.Instance, Identity(TenantRole.Owner));

        var act = async () => await editorBin.PurgeAsync("foreign", "local");

        await act.Should().ThrowAsync<UnauthorizedAccessException>("purging foreign rules needs Owner");
        (await ownerBin.PurgeAsync("foreign", "local")).Should().BeTrue("Owners may purge foreign rules");
    }

    [Fact]
    public async Task BatchService_Editor_OnlyOwnRules()
    {
        var graph = new Mock<IKnowledgeGraph>();
        graph.Setup(g => g.GetRuleAsync("own", "local", It.IsAny<CancellationToken>()))
             .ReturnsAsync(Rule("own"));
        graph.Setup(g => g.GetRuleAsync("foreign", "local", It.IsAny<CancellationToken>()))
             .ReturnsAsync(Rule("foreign", owner: "someone-else"));
        graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);
        var sut = new RuleBatchService(
            graph.Object, Mock.Of<IAuditLog>(), NullLogger<RuleBatchService>.Instance,
            new RuleAuthorizer(Identity(TenantRole.Editor)));

        var result = await sut.ApplyAsync(
            new BatchRuleOperation { Type = BatchRuleOperationType.AddTag, Tag = "t" },
            ["own", "foreign"], "local", isAdmin: false);

        result.Updated.Should().Be(1, "only the editor's own rule is modified");
        result.Skipped.Should().Be(1, "the foreign rule is skipped like a pre-C2 ownership miss");
        result.Failed.Should().Be(0);
        graph.Verify(g => g.UpsertRuleAsync(
            It.Is<KnowledgeRule>(r => r.Id == "own"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
