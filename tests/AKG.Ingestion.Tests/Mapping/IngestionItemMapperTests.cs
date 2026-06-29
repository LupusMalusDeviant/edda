using Edda.AKG.Ingestion.Mapping;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Mapping;

/// <summary>Unit tests for <see cref="IngestionItemMapper"/>.</summary>
public sealed class IngestionItemMapperTests
{
    private readonly IngestionItemMapper _mapper = new();

    private static IngestionItem Item(
        string id = "git:my-repo:docs/adr/0001-foo",
        string? relativePath = "docs/adr/0001-foo.md",
        IReadOnlyList<IngestionLink>? links = null,
        IReadOnlyDictionary<string, string>? frontmatter = null)
        => new()
        {
            Id = id,
            Title = "Title",
            Body = "Body",
            SourceKind = "git",
            RelativePath = relativePath,
            Tags = ["docs", "adr"],
            NativeLinks = links ?? [],
            RawFrontmatter = frontmatter ?? new Dictionary<string, string>(),
        };

    [Fact]
    public void Map_NoMatchingRule_DefaultsToWorldKnowledge()
    {
        var rule = _mapper.Map(Item(), [], new HashSet<string>());

        rule.Type.Should().Be("WorldKnowledge");
        rule.Priority.Should().Be(RulePriority.Medium);
        rule.OwnerId.Should().BeNull();
        rule.SourceType.Should().Be("git");
    }

    [Fact]
    public void Map_MatchingRule_AppliesTypeDomainAndPriority()
    {
        var rules = new[]
        {
            new TypeMappingRule
            {
                GlobPattern = "docs/adr/**",
                Type = "Constraint",
                Domain = "architecture",
                Priority = RulePriority.High,
            },
        };

        var rule = _mapper.Map(Item(), rules, new HashSet<string>());

        rule.Type.Should().Be("Constraint");
        rule.Domain.Should().Be("architecture");
        rule.Priority.Should().Be(RulePriority.High);
    }

    [Fact]
    public void Map_MatchingRuleWithoutDomain_DerivesDomainFromPath()
    {
        var rules = new[]
        {
            new TypeMappingRule { GlobPattern = "docs/**", Type = "WorldKnowledge" },
        };

        var rule = _mapper.Map(Item(), rules, new HashSet<string>());

        rule.Domain.Should().Be("docs");
    }

    [Fact]
    public void Map_TopLevelFile_DomainIsGeneral()
    {
        var rule = _mapper.Map(Item(relativePath: "README.md"), [], new HashSet<string>());

        rule.Domain.Should().Be("general");
    }

    [Fact]
    public void Map_FirstMatchingRuleWins()
    {
        var rules = new[]
        {
            new TypeMappingRule { GlobPattern = "docs/adr/**", Type = "Constraint", Domain = "architecture" },
            new TypeMappingRule { GlobPattern = "docs/**", Type = "WorldKnowledge", Domain = "docs" },
        };

        var rule = _mapper.Map(Item(), rules, new HashSet<string>());

        rule.Type.Should().Be("Constraint");
    }

    [Fact]
    public void Map_KnownRelatedLink_BecomesRelatedRelation()
    {
        var links = new[] { new IngestionLink { Kind = "related", TargetRef = "git:my-repo:docs/guide" } };
        var known = new HashSet<string> { "git:my-repo:docs/guide" };

        var rule = _mapper.Map(Item(links: links), [], known);

        rule.RelatesTo.Should().NotBeNull();
        rule.RelatesTo!.Related.Should().ContainSingle().Which.Should().Be("git:my-repo:docs/guide");
    }

    [Fact]
    public void Map_UnknownLinkTarget_IsIgnored()
    {
        var links = new[] { new IngestionLink { Kind = "related", TargetRef = "git:my-repo:missing" } };

        var rule = _mapper.Map(Item(links: links), [], new HashSet<string>());

        rule.RelatesTo.Should().BeNull();
    }

    [Fact]
    public void Map_SupersedesLink_MapsToSupersedesRelation()
    {
        var links = new[] { new IngestionLink { Kind = "supersedes", TargetRef = "git:my-repo:docs/adr/0000-old" } };
        var known = new HashSet<string> { "git:my-repo:docs/adr/0000-old" };

        var rule = _mapper.Map(Item(links: links), [], known);

        rule.RelatesTo!.Supersedes.Should().ContainSingle().Which.Should().Be("git:my-repo:docs/adr/0000-old");
        rule.RelatesTo.Related.Should().BeEmpty();
    }

    [Fact]
    public void Map_CarriesAuthorAndCreatedFromFrontmatter()
    {
        var frontmatter = new Dictionary<string, string>
        {
            ["author"] = "platform-team",
            ["created"] = "2025-01-15",
        };

        var rule = _mapper.Map(Item(frontmatter: frontmatter), [], new HashSet<string>());

        rule.Author.Should().Be("platform-team");
        rule.Created.Should().Be(new DateOnly(2025, 1, 15));
    }

    [Fact]
    public void Map_ItemWithExplicitDomain_OverridesRuleAndPath()
    {
        var rules = new[]
        {
            new TypeMappingRule { GlobPattern = "docs/adr/**", Type = "Constraint", Domain = "architecture" },
        };

        var rule = _mapper.Map(Item() with { Domain = "git-knowledge" }, rules, new HashSet<string>());

        rule.Domain.Should().Be("git-knowledge");
    }

    [Fact]
    public void Map_PreservesIdTagsAndBody()
    {
        var rule = _mapper.Map(Item(), [], new HashSet<string>());

        rule.Id.Should().Be("git:my-repo:docs/adr/0001-foo");
        rule.Body.Should().Be("Body");
        rule.Tags.Should().BeEquivalentTo("docs", "adr");
    }
}
