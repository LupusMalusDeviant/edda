using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Web.Services;

namespace Edda.Web.Tests;

/// <summary>Unit tests for <see cref="EntityBrowser"/> (E9 /entities search + relations).</summary>
public sealed class EntityBrowserTests
{
    private const int ExpectedSearchLimit = 50;
    private const int ExpectedRelatedLimit = 25;

    private static GraphEntity Entity(string name, string type = "concept", int mentions = 1)
        => new() { Name = name, Type = type, Mentions = mentions };

    // ── SplitTerms (pure) ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void SplitTerms_BlankQuery_ReturnsEmpty(string? query)
        => EntityBrowser.SplitTerms(query).Should().BeEmpty();

    [Fact]
    public void SplitTerms_SingleTerm_LowercasesIt()
        => EntityBrowser.SplitTerms("Neo4j").Should().ContainSingle().Which.Should().Be("neo4j");

    [Fact]
    public void SplitTerms_MultipleTerms_SplitsAndLowercases()
        => EntityBrowser.SplitTerms("Graph Store").Should().Equal("graph", "store");

    [Fact]
    public void SplitTerms_DuplicateTerms_Deduplicates()
        => EntityBrowser.SplitTerms("graph graph store").Should().Equal("graph", "store");

    [Fact]
    public void SplitTerms_ExtraWhitespace_TrimsAndIgnoresEmpty()
        => EntityBrowser.SplitTerms("  graph    store  ").Should().Equal("graph", "store");

    // ── SearchAsync ──

    [Fact]
    public async Task SearchAsync_NullStore_ReturnsEmpty()
    {
        var browser = new EntityBrowser(entities: null, new FakeIdentity("u1"));

        (await browser.SearchAsync("graph")).Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_BlankQuery_ReturnsEmptyWithoutStoreCall()
    {
        var store = new FakeEntityStore();
        var browser = new EntityBrowser(store, new FakeIdentity("u1"));

        var result = await browser.SearchAsync("   ");

        result.Should().BeEmpty();
        store.LastFind.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_PassesTermsUserIdAndLimit()
    {
        var store = new FakeEntityStore();
        var browser = new EntityBrowser(store, new FakeIdentity("user-42"));

        await browser.SearchAsync("Graph Store");

        store.LastFind.Should().NotBeNull();
        store.LastFind!.Value.Terms.Should().Equal("graph", "store");
        store.LastFind.Value.UserId.Should().Be("user-42");
        store.LastFind.Value.Limit.Should().Be(ExpectedSearchLimit);
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_ReturnsStoreResults()
    {
        var store = new FakeEntityStore { FindResult = [Entity("Graph"), Entity("Store")] };
        var browser = new EntityBrowser(store, new FakeIdentity("u1"));

        var result = await browser.SearchAsync("graph");

        result.Should().HaveCount(2).And.Contain(e => e.Name == "Graph");
    }

    [Fact]
    public async Task SearchAsync_NullIdentity_PassesNullUserId()
    {
        var store = new FakeEntityStore();
        var browser = new EntityBrowser(store, identity: null);

        await browser.SearchAsync("graph");

        store.LastFind!.Value.UserId.Should().BeNull();
    }

    // ── GetRelatedAsync ──

    [Fact]
    public async Task GetRelatedAsync_NullStore_ReturnsEmpty()
    {
        var browser = new EntityBrowser(entities: null, new FakeIdentity("u1"));

        (await browser.GetRelatedAsync("Graph")).Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelatedAsync_ValidName_PassesNameUserIdAndLimit()
    {
        var store = new FakeEntityStore();
        var browser = new EntityBrowser(store, new FakeIdentity("user-42"));

        await browser.GetRelatedAsync("Graph");

        store.LastRelated.Should().NotBeNull();
        store.LastRelated!.Value.Name.Should().Be("Graph");
        store.LastRelated.Value.UserId.Should().Be("user-42");
        store.LastRelated.Value.Limit.Should().Be(ExpectedRelatedLimit);
    }

    [Fact]
    public async Task GetRelatedAsync_ValidName_ReturnsStoreResults()
    {
        var store = new FakeEntityStore { RelatedResult = [Entity("Store")] };
        var browser = new EntityBrowser(store, new FakeIdentity("u1"));

        var result = await browser.GetRelatedAsync("Graph");

        result.Should().ContainSingle().Which.Name.Should().Be("Store");
    }

    // ── Fakes (no infrastructure) ──

    private sealed class FakeEntityStore : IEntityStore
    {
        public (IReadOnlyList<string> Terms, string? UserId, int Limit)? LastFind { get; private set; }
        public (string Name, string? UserId, int Limit)? LastRelated { get; private set; }
        public IReadOnlyList<GraphEntity> FindResult { get; init; } = [];
        public IReadOnlyList<GraphEntity> RelatedResult { get; init; } = [];

        public Task<EntityIngestionResult> IngestAsync(
            EntityExtractionResult extraction, string userId, string sourceType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<GraphEntity>> FindEntitiesAsync(
            IReadOnlyList<string> terms, string? userId, int limit = 20,
            CancellationToken cancellationToken = default)
        {
            LastFind = (terms, userId, limit);
            return Task.FromResult(FindResult);
        }

        public Task<IReadOnlyList<GraphEntity>> GetRelatedAsync(
            string entityName, string? userId, int limit = 20,
            CancellationToken cancellationToken = default)
        {
            LastRelated = (entityName, userId, limit);
            return Task.FromResult(RelatedResult);
        }
    }

    private sealed class FakeIdentity(string? userId) : IIdentityContext
    {
        public string? UserId { get; } = userId;
        public string? Username => null;
        public string TenantId => "default";
        public bool IsClone => false;
        public bool IsAdmin => false;
        public TenantRole Role => TenantRole.Viewer;
    }
}
