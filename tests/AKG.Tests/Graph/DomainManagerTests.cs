using Edda.AKG.Graph;
using Edda.AKG.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Edda.AKG.Tests.Graph;

public class DomainManagerTests
{
    private readonly FakeCypherExecutor _cypher = new();
    private readonly Mock<ILogger<DomainManager>> _logger = new();

    private DomainManager CreateManager() => new(_cypher, _logger.Object);

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> EmptyRows()
        => Array.Empty<IReadOnlyDictionary<string, object?>>();

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> CountRows(long count)
        => new[]
        {
            (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["cnt"] = count }
        };

    [Fact]
    public async Task ExistsAsync_DomainFound_ReturnsTrue()
    {
        _cypher.DefaultResult = CountRows(1L);
        var manager = CreateManager();

        var result = await manager.ExistsAsync("csharp");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_DomainNotFound_ReturnsFalse()
    {
        _cypher.DefaultResult = CountRows(0L);
        var manager = CreateManager();

        var result = await manager.ExistsAsync("nonexistent-domain");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CreateDomainAsync_ExecutesCypher_ReturnsDomainNode()
    {
        var manager = CreateManager();

        var result = await manager.CreateDomainAsync(
            name: "testing",
            label: "Testing",
            parentDomain: "general",
            description: "Test domain",
            ownerId: null);

        _cypher.ExecutedWriteQueries.Should().HaveCount(1);
        result.Name.Should().Be("testing");
        result.Label.Should().Be("Testing");
        result.ParentDomain.Should().Be("general");
        result.IsCore.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDomainAsync_CallsExecuteAsync()
    {
        var manager = CreateManager();

        await manager.DeleteDomainAsync("obsolete-domain");

        _cypher.ExecutedWriteQueries.Should().HaveCount(1);
        _cypher.ExecutedWriteQueries[0].Should().Contain("DETACH DELETE");
    }

    [Fact]
    public async Task GetDomainTreeAsync_NoNodes_ReturnsEmptyList()
    {
        _cypher.DefaultResult = EmptyRows();
        var manager = CreateManager();

        var result = await manager.GetDomainTreeAsync();

        result.Should().BeEmpty();
    }
}
