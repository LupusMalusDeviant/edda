using Edda.Agent.Tools.Memory;

namespace Edda.Agent.Tests.Tools.Memory;

public class MemorySimilarityTests
{
    [Fact]
    public void Jaccard_IdenticalContent_ReturnsOne()
    {
        MemorySimilarity.Jaccard("Bob prefers dark mode", "Bob prefers dark mode")
            .Should().Be(1.0);
    }

    [Fact]
    public void Jaccard_DisjointContent_ReturnsZero()
    {
        MemorySimilarity.Jaccard("apples oranges pears", "planets comets moons")
            .Should().Be(0.0);
    }

    [Fact]
    public void Jaccard_PartialOverlap_ReturnsIntersectionOverUnion()
    {
        // A = {the, sky, is, blue}, B = {the, sky, is, grey}: intersection 3, union 5.
        MemorySimilarity.Jaccard("the sky is blue", "the sky is grey")
            .Should().BeApproximately(3.0 / 5.0, 1e-9);
    }

    [Fact]
    public void Jaccard_BothEmpty_ReturnsZero()
    {
        MemorySimilarity.Jaccard("", "   ")
            .Should().Be(0.0);
    }

    [Fact]
    public void Jaccard_OneEmpty_ReturnsZero()
    {
        MemorySimilarity.Jaccard("something here", "")
            .Should().Be(0.0);
    }

    [Fact]
    public void Jaccard_IgnoresCaseAndPunctuation()
    {
        MemorySimilarity.Jaccard("Bob PREFERS dark-mode!", "bob prefers dark mode")
            .Should().Be(1.0);
    }

    [Fact]
    public void Tokenize_SplitsOnNonAlphanumericBoundaries_AndLowercases()
    {
        MemorySimilarity.Tokenize("Hello, World! 42_x")
            .Should().BeEquivalentTo("hello", "world", "42", "x");
    }

    [Fact]
    public void Tokenize_Deduplicates_RepeatedTokens()
    {
        MemorySimilarity.Tokenize("go Go GO")
            .Should().ContainSingle().Which.Should().Be("go");
    }

    [Fact]
    public void Tokenize_WhitespaceOnly_ReturnsEmpty()
    {
        MemorySimilarity.Tokenize("   ").Should().BeEmpty();
    }
}
