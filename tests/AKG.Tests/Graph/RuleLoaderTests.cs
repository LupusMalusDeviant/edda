using Edda.AKG.Graph;
using Edda.AKG.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Edda.AKG.Tests.Graph;

public class RuleLoaderTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeCypherExecutor _cypher = new();
    private readonly Mock<ILogger<RuleLoader>> _logger = new();

    private RuleLoader CreateLoader() => new(_fs, _cypher, TimeProvider.System, _logger.Object);

    [Fact]
    public async Task LoadFromDirectoryAsync_RuleWithRelated_UpsertsRelatedEdge()
    {
        _fs.AddFile(
            "knowledge/world-api.md",
            """
            ---
            id: world-api
            title: API Design
            domain: docs
            related: [world-oop]
            ---
            Body.
            """);
        var loader = CreateLoader();

        var loaded = await loader.LoadFromDirectoryAsync("knowledge", CancellationToken.None);

        loaded.Should().Be(1);
        // C9: the loader issues the same batched temporal replace as Neo4jKnowledgeGraph — one
        // round-trip per relation type, closing dropped edges instead of deleting them.
        var edgeQuery = _cypher.ExecutedWriteQueries.Single(q => q.Contains("MERGE (s)-[e:RELATED]"));
        edgeQuery.Should().Contain("UNWIND $targetIds AS targetId");
        edgeQuery.Should().Contain("SET stale.validUntil = $now");
        edgeQuery.Should().Contain("ON CREATE SET e.validFrom = $now");
        edgeQuery.Should().NotContain("DELETE e");
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_MissingDirectory_ReturnsZero()
    {
        var loader = CreateLoader();

        var loaded = await loader.LoadFromDirectoryAsync("does-not-exist", CancellationToken.None);

        loaded.Should().Be(0);
        _cypher.ExecutedWriteQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_RuleWithValidatorScript_PersistsIt()
    {
        _fs.AddFile(
            "knowledge/sec.md",
            """
            ---
            id: sec
            title: Sec
            domain: security
            validatorScript: |
              print("hi")
            ---
            Body.
            """);
        var loader = CreateLoader();

        await loader.LoadFromDirectoryAsync("knowledge", CancellationToken.None);

        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("r.validatorScript = $validatorScript"),
            because: "F1: the upsert must persist the validator script");
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_RuleWithValidator_PersistsEnabledFlagAndHash()
    {
        _fs.AddFile(
            "knowledge/sec.md",
            """
            ---
            id: sec
            title: Sec
            domain: security
            validatorScript: |
              print("hi")
            ---
            Body.
            """);

        await CreateLoader().LoadFromDirectoryAsync("knowledge", CancellationToken.None);

        _cypher.ExecutedWriteQueries.Should().Contain(
            q => q.Contains("r.validatorEnabled = $validatorEnabled") && q.Contains("r.validatorHash = $validatorHash"),
            because: "F7: the upsert must persist the kill-switch flag and the script hash");
    }
}
