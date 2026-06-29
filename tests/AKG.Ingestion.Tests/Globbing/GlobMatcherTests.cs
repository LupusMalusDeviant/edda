using Edda.AKG.Ingestion.Globbing;

namespace Edda.AKG.Ingestion.Tests.Globbing;

/// <summary>Unit tests for <see cref="GlobMatcher"/>.</summary>
public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("docs/**", "docs/adr/0001-foo.md", true)]
    [InlineData("docs/**", "docs/guide.md", true)]
    [InlineData("docs/**", "src/code.cs", false)]
    [InlineData("**/*.md", "docs/adr/x.md", true)]
    [InlineData("*.md", "README.md", true)]
    [InlineData("*.md", "docs/x.md", false)]
    [InlineData("README*", "README.md", true)]
    [InlineData("docs/adr/**", "docs/adr/0001.md", true)]
    [InlineData("docs/adr/**", "docs/guide.md", false)]
    public void IsMatch_EvaluatesGlob(string glob, string path, bool expected)
    {
        GlobMatcher.IsMatch(glob, path).Should().Be(expected);
    }

    [Fact]
    public void IsMatch_EmptyGlob_ReturnsFalse()
    {
        GlobMatcher.IsMatch(string.Empty, "anything").Should().BeFalse();
    }

    [Fact]
    public void IsMatch_NormalizesBackslashes()
    {
        GlobMatcher.IsMatch("docs/**", "docs\\adr\\x.md").Should().BeTrue();
    }
}
