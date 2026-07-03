using Edda.AKG.Background;
using Edda.AKG.Graph;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Graph;

public sealed class WorldKnowledgeSeedHostedServiceTests
{
    private readonly InMemoryFileSystem _fileSystem = new();
    private readonly FakeCypherExecutor _cypher = new();
    private readonly Mock<IKnowledgeGraph> _graphMock = new();
    private readonly ChannelBackgroundWorkQueue _workQueue = new();
    private readonly WorldKnowledgeSeedHostedService _sut;

    public WorldKnowledgeSeedHostedServiceTests()
    {
        // Construct real (sealed) instances backed by fakes.
        // InMemoryFileSystem is empty → SeedIfEmptyAsync / LoadFromDirectoryAsync return 0.
        var seeder = new WorldKnowledgeSeeder(
            _fileSystem,
            _cypher,
            NullLogger<WorldKnowledgeSeeder>.Instance);

        var ruleLoader = new RuleLoader(
            _fileSystem,
            _cypher,
            TimeProvider.System,
            NullLogger<RuleLoader>.Instance);

        _graphMock.Setup(g => g.RebuildEmbeddingsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new WorldKnowledgeSeedHostedService(
            seeder,
            ruleLoader,
            _graphMock.Object,
            _fileSystem,
            _cypher,
            _workQueue,
            NullLogger<WorldKnowledgeSeedHostedService>.Instance);
    }

    [Fact]
    public async Task StartAsync_SeedsToolDomains()
    {
        await _sut.StartAsync(CancellationToken.None);

        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("'tools'") && q.Contains("'custom-tools'") && q.Contains("HAS_SUBDOMAIN"));
    }

    [Fact]
    public async Task StartAsync_EnqueuesSupersededRuleInvalidation()
    {
        _graphMock.Setup(g => g.InvalidateSupersededRulesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.StartAsync(CancellationToken.None);

        // The invalidation is queued (not run inline / not detached via Task.Run). Draining the queue
        // and running the item must invoke InvalidateSupersededRulesAsync.
        var item = await _workQueue.DequeueAsync(CancellationToken.None);
        await item.Work(CancellationToken.None);

        _graphMock.Verify(g => g.InvalidateSupersededRulesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_DomainsExist_Idempotent()
    {
        // Run twice — should not throw
        await _sut.StartAsync(CancellationToken.None);
        await _sut.StartAsync(CancellationToken.None);

        // MERGE is idempotent, so both calls succeed
        _cypher.ExecutedWriteQueries.Count(q => q.Contains("'tools'")).Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task SeedToolDomainsAsync_SetsIsCoreTrueAndLabels()
    {
        await _sut.SeedToolDomainsAsync(CancellationToken.None);

        _cypher.ExecutedWriteQueries.Should().Contain(q =>
            q.Contains("isCore = true")
            && q.Contains("Tool-Dokumentation")
            && q.Contains("Benutzerdefinierte Tools"));
    }

    [Fact]
    public async Task SeedKnowledgeDirectoryIfEmptyAsync_EmptyKnowledge_CopiesBundlePreservingLayout()
    {
        _fileSystem.AddFile("knowledge.seed/rule.md", "# Rule");
        _fileSystem.AddFile("knowledge.seed/world/fact.md", "# Fact");

        var copied = await _sut.SeedKnowledgeDirectoryIfEmptyAsync(CancellationToken.None);

        copied.Should().Be(2);
        _fileSystem.FileExists("knowledge/rule.md").Should().BeTrue();
        _fileSystem.FileExists("knowledge/world/fact.md").Should().BeTrue();
        (await _fileSystem.ReadAllTextAsync("knowledge/world/fact.md")).Should().Be("# Fact");
    }

    [Fact]
    public async Task SeedKnowledgeDirectoryIfEmptyAsync_KnowledgeAlreadyPopulated_DoesNotCopy()
    {
        _fileSystem.AddFile("knowledge/existing.md", "# Existing");
        _fileSystem.AddFile("knowledge.seed/rule.md", "# Bundle");

        var copied = await _sut.SeedKnowledgeDirectoryIfEmptyAsync(CancellationToken.None);

        copied.Should().Be(0);
        _fileSystem.FileExists("knowledge/rule.md").Should().BeFalse();
    }

    [Fact]
    public async Task SeedKnowledgeDirectoryIfEmptyAsync_NoBundle_ReturnsZero()
    {
        var copied = await _sut.SeedKnowledgeDirectoryIfEmptyAsync(CancellationToken.None);

        copied.Should().Be(0);
    }

    [Fact]
    public async Task SeedToolDomainsAsync_CreatesAllToolboxSubDomains()
    {
        await _sut.SeedToolDomainsAsync(CancellationToken.None);

        // 1 query for tools+custom-tools + 11 queries for toolbox sub-domains = 12
        _cypher.ExecutedWriteQueries.Count.Should().Be(1 + WorldKnowledgeSeedHostedService.ToolboxDomains.Count);

        // Verify each toolbox sub-domain is created
        foreach (var (name, _) in WorldKnowledgeSeedHostedService.ToolboxDomains)
        {
            _cypher.ExecutedWriteQueries.Should().Contain(q =>
                q.Contains("$name") && q.Contains("HAS_SUBDOMAIN"));
        }
    }
}
