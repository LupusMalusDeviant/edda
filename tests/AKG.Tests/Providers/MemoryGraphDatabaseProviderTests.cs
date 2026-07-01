using Edda.AKG.Graph;
using Edda.AKG.Providers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edda.AKG.Tests.Providers;

/// <summary>Unit tests for <see cref="MemoryGraphDatabaseProvider"/> (zero-infra dev-mode graph).</summary>
public sealed class MemoryGraphDatabaseProviderTests
{
    private readonly MemoryGraphDatabaseProvider _sut =
        new(new NullLoggerFactory());

    [Fact]
    public void Name_IsMemory()
        => _sut.Name.Should().Be("memory");

    [Fact]
    public async Task IsHealthyAsync_AlwaysTrue()
        => (await _sut.IsHealthyAsync()).Should().BeTrue();

    [Fact]
    public void CreateExecutor_ReturnsSharedInMemoryExecutor()
    {
        var a = _sut.CreateExecutor();
        var b = _sut.CreateExecutor();

        a.Should().BeOfType<InMemoryCypherExecutor>();
        b.Should().BeSameAs(a); // one shared executor → state persists across calls
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var act = async () => await _sut.DisposeAsync();
        await act.Should().NotThrowAsync();
    }
}
