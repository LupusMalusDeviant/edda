using Edda.AKG.Ingestion.Enrichment;
using Edda.AKG.Ingestion.Pipeline;
using Edda.AKG.Ingestion.Sync;
using Edda.AKG.Ingestion.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Pipeline;

/// <summary>Unit tests for <see cref="IngestionPipeline"/>.</summary>
public sealed class IngestionPipelineTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly InMemoryFileSystem _fs = new();
    private readonly List<KnowledgeRule> _upserted = [];
    private readonly Mock<IEntityIngestionService> _entityIngestion = new();
    private readonly Mock<IIdentityContext> _identity = new();
    private readonly Mock<IConfiguration> _configuration = new();

    public IngestionPipelineTests()
    {
        _graph
            .Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeRule rule, CancellationToken _) =>
            {
                _upserted.Add(rule);
                return rule;
            });
        _identity.SetupGet(i => i.UserId).Returns("local");
        _configuration.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);
        _entityIngestion
            .Setup(e => e.IngestTextAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityIngestionResult.Empty);
    }

    private static IngestionItem Item(
        string id,
        string relativePath,
        IReadOnlyList<IngestionLink>? links = null)
        => new()
        {
            Id = id,
            Title = "Title",
            Body = "Body",
            SourceKind = "git",
            RelativePath = relativePath,
            NativeLinks = links ?? [],
        };

    private IngestionPipeline CreatePipeline(IIngestionEnricher enricher, params IngestionItem[] items)
        => new(
            [new FakeIngestionSource("git", items)], enricher, _fs, _graph.Object,
            _entityIngestion.Object, _identity.Object, _configuration.Object);

    private IngestionPipeline CreatePipelineWithStore(ISyncStateStore syncState, params IngestionItem[] items)
        => new(
            [new FakeIngestionSource("git", items)], new NullIngestionEnricher(), _fs, _graph.Object,
            _entityIngestion.Object, _identity.Object, _configuration.Object, syncState);

    private static IngestionRequest Request(bool enrich = false, string? target = null)
        => new() { SourceKind = "git", Source = new IngestionSourceConfig(), EnableEnrichment = enrich, TargetDirectory = target };

    [Fact]
    public async Task IngestAsync_WritesMarkdownAndUpsertsRule()
    {
        var pipeline = CreatePipeline(new NullIngestionEnricher(), Item("git:r:docs/x", "docs/x.md"));

        var result = await pipeline.IngestAsync(Request());

        result.Imported.Should().Be(1);
        result.Failed.Should().Be(0);
        _upserted.Should().ContainSingle().Which.Id.Should().Be("git:r:docs/x");
        _fs.FileExists("knowledge/ingested/docs/git-r-docs-x.md").Should().BeTrue();
    }

    [Fact]
    public async Task IngestAsync_EntersBulkIngestionScope_AndDisposesIt()
    {
        var disposed = false;
        var scope = new Mock<IDisposable>();
        scope.Setup(d => d.Dispose()).Callback(() => disposed = true);
        _graph.Setup(g => g.BeginBulkIngestion()).Returns(scope.Object);

        var pipeline = CreatePipeline(new NullIngestionEnricher(), Item("git:r:docs/x", "docs/x.md"));
        await pipeline.IngestAsync(Request());

        _graph.Verify(g => g.BeginBulkIngestion(), Times.Once);
        disposed.Should().BeTrue();
    }

    [Fact]
    public async Task IngestAsync_UnknownSourceKind_ReturnsError()
    {
        var pipeline = CreatePipeline(new NullIngestionEnricher());

        var result = await pipeline.IngestAsync(
            new IngestionRequest { SourceKind = "jira", Source = new IngestionSourceConfig() });

        result.Imported.Should().Be(0);
        result.Failed.Should().BeGreaterThan(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("jira");
    }

    [Fact]
    public async Task IngestAsync_SourceThrows_ReportsFailureWithReason()
    {
        var pipeline = new IngestionPipeline(
            [new ThrowingIngestionSource()], new NullIngestionEnricher(), _fs, _graph.Object,
            _entityIngestion.Object, _identity.Object, _configuration.Object);

        var result = await pipeline.IngestAsync(Request());

        // A source-level failure must surface as a failure with its reason — not a silent "0 imported".
        result.Failed.Should().BeGreaterThan(0);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("simulated GitLab 404");
    }

    /// <summary>Ingestion source whose <c>FetchAsync</c> fails, to exercise the pipeline's error path.</summary>
    private sealed class ThrowingIngestionSource : IIngestionSource
    {
        public string SourceKind => "git";

        public IAsyncEnumerable<IngestionItem> FetchAsync(
            IngestionSourceConfig config, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated GitLab 404");
    }

    [Fact]
    public async Task IngestAsync_ResolvesRelationsAcrossItems()
    {
        var a = Item("git:r:a", "a.md", [new IngestionLink { Kind = "related", TargetRef = "git:r:b" }]);
        var b = Item("git:r:b", "b.md");
        var pipeline = CreatePipeline(new NullIngestionEnricher(), a, b);

        await pipeline.IngestAsync(Request());

        var ruleA = _upserted.Single(r => r.Id == "git:r:a");
        ruleA.RelatesTo.Should().NotBeNull();
        ruleA.RelatesTo!.Related.Should().Contain("git:r:b");
    }

    [Fact]
    public async Task IngestAsync_EnrichmentDisabled_DoesNotInvokeEnricher()
    {
        var enricher = new Mock<IIngestionEnricher>();
        var pipeline = CreatePipeline(enricher.Object, Item("git:r:x", "x.md"));

        await pipeline.IngestAsync(Request(enrich: false));

        enricher.Verify(
            e => e.EnrichAsync(It.IsAny<IngestionItem>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestAsync_EnrichmentEnabled_InvokesEnricher()
    {
        var enricher = new Mock<IIngestionEnricher>();
        enricher
            .Setup(e => e.EnrichAsync(It.IsAny<IngestionItem>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionItem item, IReadOnlyCollection<string> _, CancellationToken _) => item);
        var pipeline = CreatePipeline(enricher.Object, Item("git:r:x", "x.md"));

        await pipeline.IngestAsync(Request(enrich: true));

        enricher.Verify(
            e => e.EnrichAsync(It.IsAny<IngestionItem>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IngestAsync_GraphUpsertThrows_RecordsErrorAndContinues()
    {
        _graph
            .Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var pipeline = CreatePipeline(new NullIngestionEnricher(), Item("git:r:x", "x.md"));

        var result = await pipeline.IngestAsync(Request());

        result.Imported.Should().Be(0);
        result.Failed.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.ItemId.Should().Be("git:r:x");
    }

    [Fact]
    public async Task IngestAsync_UsesConfiguredTargetDirectory()
    {
        var pipeline = CreatePipeline(new NullIngestionEnricher(), Item("git:r:docs/x", "docs/x.md"));

        await pipeline.IngestAsync(Request(target: "custom/out"));

        _fs.FileExists("custom/out/docs/git-r-docs-x.md").Should().BeTrue();
    }

    [Fact]
    public async Task IngestAsync_EntityExtractionDisabled_DoesNotInvokeEntityIngestion()
    {
        var pipeline = CreatePipeline(new NullIngestionEnricher(), Item("git:r:x", "x.md"));

        await pipeline.IngestAsync(Request());

        _entityIngestion.Verify(
            e => e.IngestTextAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestAsync_EntityExtractionEnabled_IngestsEntitiesPerItem_ScopedToIdentity()
    {
        _configuration.Setup(c => c["INGESTION_ENTITY_EXTRACTION"]).Returns("true");
        var pipeline = CreatePipeline(new NullIngestionEnricher(), Item("git:r:a", "a.md"), Item("git:r:b", "b.md"));

        await pipeline.IngestAsync(Request());

        _entityIngestion.Verify(
            e => e.IngestTextAsync("Body", It.IsAny<string?>(), "local", "git", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task IngestAsync_SecondRunWithoutChanges_ImportsZeroAndSkipsAll()
    {
        // C5 acceptance: a second run over an unchanged (fake git) source ingests 0 items.
        var store = new FileSyncStateStore(_fs);
        var pipeline = CreatePipelineWithStore(store, Item("git:r:a", "a.md"), Item("git:r:b", "b.md"));

        var first = await pipeline.IngestAsync(Request());
        var second = await pipeline.IngestAsync(Request());

        first.Imported.Should().Be(2);
        second.Imported.Should().Be(0);
        second.Skipped.Should().Be(2);
        _upserted.Should().HaveCount(2);   // the second run re-upserts nothing
    }

    [Fact]
    public async Task IngestAsync_ChangedItem_ReimportsOnlyChanged()
    {
        var store = new FileSyncStateStore(_fs);
        await CreatePipelineWithStore(store, Item("git:r:a", "a.md"), Item("git:r:b", "b.md")).IngestAsync(Request());

        var changedA = new IngestionItem
        {
            Id = "git:r:a", Title = "Title", Body = "CHANGED", SourceKind = "git", RelativePath = "a.md",
        };
        var result = await CreatePipelineWithStore(store, changedA, Item("git:r:b", "b.md")).IngestAsync(Request());

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task IngestAsync_ForceFullSync_ReimportsEverything()
    {
        var store = new FileSyncStateStore(_fs);
        var pipeline = CreatePipelineWithStore(store, Item("git:r:a", "a.md"), Item("git:r:b", "b.md"));

        await pipeline.IngestAsync(Request());
        var forced = await pipeline.IngestAsync(Request() with { ForceFullSync = true });

        forced.Imported.Should().Be(2);
        forced.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task IngestAsync_WithoutSyncStateStore_AlwaysFullIngest()
    {
        var pipeline = CreatePipeline(new NullIngestionEnricher(), Item("git:r:a", "a.md"));

        var first = await pipeline.IngestAsync(Request());
        var second = await pipeline.IngestAsync(Request());

        first.Imported.Should().Be(1);
        second.Imported.Should().Be(1);   // no store → no skip → full ingest every run
        second.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task IngestAsync_FailedItem_NotRecordedInState_RetriedNextRun()
    {
        var store = new FileSyncStateStore(_fs);
        var pipeline = CreatePipelineWithStore(store, Item("git:r:a", "a.md"));

        _graph
            .Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var first = await pipeline.IngestAsync(Request());
        first.Failed.Should().Be(1);

        _graph
            .Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeRule rule, CancellationToken _) => rule);
        var second = await pipeline.IngestAsync(Request());

        second.Imported.Should().Be(1);   // the failed item was not recorded → retried, not skipped
        second.Skipped.Should().Be(0);
    }
}
