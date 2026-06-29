using Edda.AKG.Context;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Context;

/// <summary>
/// Unit tests for <see cref="DomainActivationResolver"/>: domain activation by name/label match,
/// downward sub-domain expansion, short-name guarding, and the empty-graph / no-match fallbacks.
/// </summary>
public class DomainActivationResolverTests
{
    private static TaskContext Ctx(string task, params string[] concepts)
        => new() { Task = task, Concepts = concepts };

    private static IReadOnlyDictionary<string, object?> Domain(
        string name, string? label = null, string? parent = null)
        => new Dictionary<string, object?> { ["name"] = name, ["label"] = label, ["parent"] = parent };

    private static DomainActivationResolver ResolverWith(params IReadOnlyDictionary<string, object?>[] domains)
    {
        var cypher = new FakeCypherExecutor();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = domains;
        cypher.AddQueryHandler(_ => rows);
        return new DomainActivationResolver(cypher, TimeProvider.System);
    }

    [Fact]
    public async Task ResolveAsync_DomainNameInConcepts_ActivatesOnlyThatDomain()
    {
        var sut = ResolverWith(Domain("security"), Domain("csharp"));

        var active = await sut.ResolveAsync(Ctx("Wie speichere ich Secrets?", "security"), CancellationToken.None);

        active.Should().Contain("security");
        active.Should().NotContain("csharp");
    }

    [Fact]
    public async Task ResolveAsync_DomainNameSubstringInTask_ActivatesDomain()
    {
        var sut = ResolverWith(Domain("security"), Domain("architecture"));

        var active = await sut.ResolveAsync(Ctx("Review the security architecture"), CancellationToken.None);

        active.Should().Contain("security");
        active.Should().Contain("architecture");
    }

    [Fact]
    public async Task ResolveAsync_DomainLabelMatches_ActivatesDomain()
    {
        var sut = ResolverWith(Domain("csharp", label: "Programmierung"));

        var active = await sut.ResolveAsync(Ctx("Frage zur Programmierung"), CancellationToken.None);

        active.Should().Contain("csharp");
    }

    [Fact]
    public async Task ResolveAsync_ActiveParent_ActivatesSubDomainsTransitively()
    {
        var sut = ResolverWith(
            Domain("technisch"),
            Domain("csharp", parent: "technisch"),
            Domain("security", parent: "technisch"));

        var active = await sut.ResolveAsync(Ctx("Allgemeine Frage", "technisch"), CancellationToken.None);

        active.Should().Contain("technisch");
        active.Should().Contain("csharp");
        active.Should().Contain("security");
    }

    [Fact]
    public async Task ResolveAsync_MatchIsCaseInsensitive()
    {
        var sut = ResolverWith(Domain("Security"));

        var active = await sut.ResolveAsync(Ctx("question about SECURITY"), CancellationToken.None);

        active.Should().Contain("Security");
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsEmpty()
    {
        var sut = ResolverWith(Domain("security"), Domain("csharp"));

        var active = await sut.ResolveAsync(Ctx("Tell me a joke about cats"), CancellationToken.None);

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ShortDomainName_RequiresExactConceptMatch()
    {
        var sut = ResolverWith(Domain("go"));

        var substringOnly = await sut.ResolveAsync(Ctx("This is a good idea"), CancellationToken.None);
        var exactConcept = await sut.ResolveAsync(Ctx("a question", "go"), CancellationToken.None);

        substringOnly.Should().BeEmpty(because: "short names must not match via substring ('go' in 'good')");
        exactConcept.Should().Contain("go");
    }

    [Fact]
    public async Task ResolveAsync_NoDomainsInGraph_ReturnsEmpty()
    {
        var sut = new DomainActivationResolver(new FakeCypherExecutor(), TimeProvider.System);

        var active = await sut.ResolveAsync(Ctx("anything", "security"), CancellationToken.None);

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_CalledTwice_LoadsDomainHierarchyOnce()
    {
        var cypher = new FakeCypherExecutor();
        var loads = 0;
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = new[] { Domain("security") };
        cypher.AddQueryHandler(_ => { loads++; return rows; });
        var sut = new DomainActivationResolver(cypher, TimeProvider.System);

        await sut.ResolveAsync(Ctx("about security"), CancellationToken.None);
        await sut.ResolveAsync(Ctx("more security"), CancellationToken.None);

        loads.Should().Be(1, because: "the domain hierarchy is cached within the TTL, sparing a round-trip per compile");
    }
}
