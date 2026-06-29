using Edda.AKG.Ingestion.Markdown;

namespace Edda.AKG.Ingestion.Tests.Markdown;

/// <summary>Unit tests for <see cref="MarkdownFrontmatter"/>.</summary>
public sealed class MarkdownFrontmatterTests
{
    [Fact]
    public void Split_WithFrontmatter_ParsesScalarsAndBody()
    {
        var content =
            """
            ---
            title: My Title
            domain: docs
            ---
            The body.
            """;

        var (frontmatter, body) = MarkdownFrontmatter.Split(content);

        frontmatter["title"].Should().Be("My Title");
        frontmatter["domain"].Should().Be("docs");
        body.Trim().Should().Be("The body.");
    }

    [Fact]
    public void Split_NoFrontmatter_ReturnsEmptyMapAndWholeBody()
    {
        var content = "# Heading\n\nJust content.";

        var (frontmatter, body) = MarkdownFrontmatter.Split(content);

        frontmatter.Should().BeEmpty();
        body.Should().Be(content);
    }

    [Fact]
    public void Split_UnterminatedFrontmatter_TreatsEverythingAsBody()
    {
        var content = "---\ntitle: T\nno closing fence";

        var (frontmatter, body) = MarkdownFrontmatter.Split(content);

        frontmatter.Should().BeEmpty();
        body.Should().Be(content);
    }

    [Fact]
    public void FirstHeading_ReturnsFirstLevelOneHeading()
    {
        MarkdownFrontmatter.FirstHeading("intro\n# The Title\n## Sub").Should().Be("The Title");
    }

    [Fact]
    public void FirstHeading_NoHeading_ReturnsNull()
    {
        MarkdownFrontmatter.FirstHeading("just text\n## only sub").Should().BeNull();
    }

    [Fact]
    public void MarkdownLinks_ReturnsOnlyMarkdownTargets_StrippingAnchors()
    {
        var body = "See [a](docs/x.md), [ext](https://example.com), [c](./z.md#section).";

        var links = MarkdownFrontmatter.MarkdownLinks(body);

        links.Should().BeEquivalentTo(new[] { "docs/x.md", "./z.md" }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void MarkdownLinks_NoLinks_ReturnsEmpty()
    {
        MarkdownFrontmatter.MarkdownLinks("plain text").Should().BeEmpty();
    }
}
