using Edda.Web.Components.AKG;
using Edda.Web.Services;

namespace Edda.Web.Tests;

/// <summary>Unit tests for <see cref="KnowledgeSearch"/> (E1 /knowledge search + filter).</summary>
public sealed class KnowledgeSearchTests
{
    private static KnowledgeGraphView.KnowledgeRuleDto Dto(
        string id,
        string domain = "coding",
        string type = "Rule",
        string body = "body",
        IReadOnlyList<string>? tags = null)
        => new(id, domain, "Medium", type, body, null, null, null, null, null, null, tags ?? []);

    [Fact]
    public void Filter_NoQueryNoFilters_ReturnsAll()
    {
        var rules = new[] { Dto("a"), Dto("b") };

        KnowledgeSearch.Filter(rules, null, null, null, null).Should().HaveCount(2);
    }

    [Fact]
    public void Filter_Query_MatchesId_CaseInsensitive()
    {
        var rules = new[] { Dto("async-await"), Dto("null-safety") };

        KnowledgeSearch.Filter(rules, "ASYNC", null, null, null)
            .Should().ContainSingle().Which.Id.Should().Be("async-await");
    }

    [Fact]
    public void Filter_Query_MatchesBody()
    {
        var rules = new[] { Dto("r1", body: "use ConfigureAwait"), Dto("r2", body: "avoid globals") };

        KnowledgeSearch.Filter(rules, "configureawait", null, null, null)
            .Should().ContainSingle().Which.Id.Should().Be("r1");
    }

    [Fact]
    public void Filter_Query_MatchesDomain()
    {
        var rules = new[] { Dto("r1", domain: "security"), Dto("r2", domain: "coding") };

        KnowledgeSearch.Filter(rules, "secur", null, null, null)
            .Should().ContainSingle().Which.Id.Should().Be("r1");
    }

    [Fact]
    public void Filter_Query_NoMatch_ReturnsEmpty()
    {
        var rules = new[] { Dto("a"), Dto("b") };

        KnowledgeSearch.Filter(rules, "zzz-nope", null, null, null).Should().BeEmpty();
    }

    [Fact]
    public void Filter_Domain_ExactMatchOnly()
    {
        var rules = new[] { Dto("r1", domain: "security"), Dto("r2", domain: "coding") };

        KnowledgeSearch.Filter(rules, null, "security", null, null)
            .Should().ContainSingle().Which.Id.Should().Be("r1");
    }

    [Fact]
    public void Filter_Type_ExactMatchOnly()
    {
        var rules = new[] { Dto("r1", type: "Constraint"), Dto("r2", type: "Rule") };

        KnowledgeSearch.Filter(rules, null, null, "Constraint", null)
            .Should().ContainSingle().Which.Id.Should().Be("r1");
    }

    [Fact]
    public void Filter_Tag_MatchesRulesCarryingTag_CaseInsensitive()
    {
        var rules = new[] { Dto("r1", tags: ["adr", "arch"]), Dto("r2", tags: ["readme"]) };

        KnowledgeSearch.Filter(rules, null, null, null, "ADR")
            .Should().ContainSingle().Which.Id.Should().Be("r1");
    }

    [Fact]
    public void Filter_CombinedQueryAndDomain_AppliesBoth()
    {
        var rules = new[]
        {
            Dto("secure-async", domain: "security", body: "async"),
            Dto("coding-async", domain: "coding", body: "async"),
            Dto("secure-other", domain: "security", body: "other"),
        };

        KnowledgeSearch.Filter(rules, "async", "security", null, null)
            .Should().ContainSingle().Which.Id.Should().Be("secure-async");
    }

    [Fact]
    public void Filter_PreservesInputOrder()
    {
        var rules = new[] { Dto("b", domain: "coding"), Dto("a", domain: "coding") };

        KnowledgeSearch.Filter(rules, null, "coding", null, null)
            .Select(r => r.Id).Should().ContainInOrder("b", "a");
    }
}
