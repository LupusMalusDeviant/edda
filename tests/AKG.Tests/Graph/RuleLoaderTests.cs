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

    private RuleLoader CreateLoader() => new(_fs, _cypher, _logger.Object);

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
        _cypher.ExecutedWriteQueries.Should().Contain(q => q.Contains("RELATED"));
    }

    [Fact]
    public async Task LoadFromDirectoryAsync_MissingDirectory_ReturnsZero()
    {
        var loader = CreateLoader();

        var loaded = await loader.LoadFromDirectoryAsync("does-not-exist", CancellationToken.None);

        loaded.Should().Be(0);
        _cypher.ExecutedWriteQueries.Should().BeEmpty();
    }
}
