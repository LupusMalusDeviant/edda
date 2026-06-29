using Edda.AKG.Ingestion.Markdown;
using Edda.AKG.Parser;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Markdown;

/// <summary>Unit tests for <see cref="FrontmatterSerializer"/>, including round-trip via the real parser.</summary>
public sealed class FrontmatterSerializerTests
{
    private readonly FrontmatterSerializer _serializer = new();

    [Fact]
    public void Serialize_ThenParse_RoundTripsParserVisibleFields()
    {
        var rule = new KnowledgeRule
        {
            Id = "git:my-repo:docs/x",
            Type = "Constraint",
            Domain = "architecture",
            Priority = RulePriority.High,
            Body = "The body.",
            Tags = ["alpha", "beta"],
            Author = "platform-team",
            Created = new DateOnly(2025, 1, 15),
            RelatesTo = new RuleRelations
            {
                Related = ["git:my-repo:docs/y"],
                Supersedes = ["git:my-repo:docs/old"],
            },
        };

        var markdown = _serializer.Serialize(rule, "My Title");
        var parsed = new KnowledgeRuleParser().Parse(markdown);

        parsed.Id.Should().Be(rule.Id);
        parsed.Type.Should().Be(rule.Type);
        parsed.Domain.Should().Be(rule.Domain);
        parsed.Priority.Should().Be(rule.Priority);
        parsed.Tags.Should().BeEquivalentTo(rule.Tags);
        parsed.Author.Should().Be(rule.Author);
        parsed.Created.Should().Be(rule.Created);
        parsed.Body.Should().Be(rule.Body);
        parsed.RelatesTo.Should().NotBeNull();
        parsed.RelatesTo!.Related.Should().BeEquivalentTo("git:my-repo:docs/y");
        parsed.RelatesTo.Supersedes.Should().BeEquivalentTo("git:my-repo:docs/old");
    }

    [Fact]
    public void Serialize_WritesTitleIntoFrontmatter()
    {
        var rule = new KnowledgeRule
        {
            Id = "id",
            Type = "WorldKnowledge",
            Domain = "general",
            Priority = RulePriority.Medium,
            Body = "Body",
        };

        var markdown = _serializer.Serialize(rule, "Readable Title");

        markdown.Should().Contain("title: Readable Title");
    }

    [Fact]
    public void Serialize_NoRelations_OmitsRelationLines()
    {
        var rule = new KnowledgeRule
        {
            Id = "id",
            Type = "WorldKnowledge",
            Domain = "general",
            Priority = RulePriority.Medium,
            Body = "Body",
        };

        var markdown = _serializer.Serialize(rule, "T");

        markdown.Should().NotContain("related:");
        markdown.Should().NotContain("supersedes:");
    }

    [Fact]
    public void Serialize_NullAuthor_OmitsAuthorLine()
    {
        var rule = new KnowledgeRule
        {
            Id = "id",
            Type = "WorldKnowledge",
            Domain = "general",
            Priority = RulePriority.Medium,
            Body = "Body",
        };

        var markdown = _serializer.Serialize(rule, "T");

        markdown.Should().NotContain("author:");
    }
}
