using Edda.AKG.Context;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Edda.AKG.Tests.Context;

/// <summary>Unit tests for <see cref="RetrievalOptionsResolver"/>.</summary>
public sealed class RetrievalOptionsResolverTests
{
    private static IConfiguration Config(Dictionary<string, string?> values)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c[It.IsAny<string>()])
            .Returns((string key) => values.TryGetValue(key, out var value) ? value : null);
        return config.Object;
    }

    [Fact]
    public void Resolve_NullConfiguration_UsesDefaults()
    {
        RetrievalOptionsResolver.Resolve(null).Should().Be(new RetrievalOptions());
    }

    [Fact]
    public void Resolve_EmptyConfiguration_UsesDefaults()
    {
        RetrievalOptionsResolver.Resolve(Config(new Dictionary<string, string?>()))
            .Should().Be(new RetrievalOptions());
    }

    [Fact]
    public void Resolve_AllValuesSet_BindsThem()
    {
        var options = RetrievalOptionsResolver.Resolve(Config(new Dictionary<string, string?>
        {
            ["RETRIEVAL_SIMILARITY_THRESHOLD"] = "0.65",
            ["RETRIEVAL_VECTOR_TOP_K"] = "250",
            ["RETRIEVAL_MMR_TOP_N"] = "20",
            ["RETRIEVAL_MMR_LAMBDA"] = "0.9",
            ["RETRIEVAL_HEAD_THRESHOLD"] = "0.55",
        }));

        options.SimilarityThreshold.Should().Be(0.65);
        options.VectorTopK.Should().Be(250);
        options.MmrTopN.Should().Be(20);
        options.MmrLambda.Should().Be(0.9);
        options.HeadSimilarityThreshold.Should().Be(0.55);
    }

    [Fact]
    public void Resolve_ExpansionKeys_ParsesValues()
    {
        var options = RetrievalOptionsResolver.Resolve(Config(new Dictionary<string, string?>
        {
            ["RETRIEVAL_QUERY_EXPANSION_TERMS"] = "3",
            ["RETRIEVAL_QUERY_EXPANSION_WEIGHT"] = "0.25",
        }));

        options.QueryExpansionTerms.Should().Be(3);
        options.QueryExpansionWeight.Should().Be(0.25);
    }

    [Fact]
    public void Resolve_ExpansionKeysUnset_UsesDisabledDefaults()
    {
        var options = RetrievalOptionsResolver.Resolve(null);

        options.QueryExpansionTerms.Should().Be(0, because: "expansion is opt-in — default off");
        options.QueryExpansionWeight.Should().Be(0.5);
    }

    [Fact]
    public void Resolve_NonNumericOrEmptyValues_FallBackToDefaults()
    {
        var options = RetrievalOptionsResolver.Resolve(Config(new Dictionary<string, string?>
        {
            ["RETRIEVAL_SIMILARITY_THRESHOLD"] = "abc",
            ["RETRIEVAL_VECTOR_TOP_K"] = "not-a-number",
            ["RETRIEVAL_MMR_LAMBDA"] = "",
            ["RETRIEVAL_HEAD_THRESHOLD"] = "   ",
        }));

        options.Should().Be(new RetrievalOptions());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Resolve_NonPositiveTopKOrTopN_FallBackToDefaults(string raw)
    {
        var options = RetrievalOptionsResolver.Resolve(Config(new Dictionary<string, string?>
        {
            ["RETRIEVAL_VECTOR_TOP_K"] = raw,
            ["RETRIEVAL_MMR_TOP_N"] = raw,
        }));

        options.VectorTopK.Should().Be(RetrievalOptions.DefaultVectorTopK);
        options.MmrTopN.Should().Be(RetrievalOptions.DefaultMmrTopN);
    }

    [Fact]
    public void Resolve_DecimalUsesInvariantCulture()
    {
        // A dot decimal separator must parse regardless of the host locale.
        var options = RetrievalOptionsResolver.Resolve(Config(new Dictionary<string, string?>
        {
            ["RETRIEVAL_SIMILARITY_THRESHOLD"] = "0.42",
        }));

        options.SimilarityThreshold.Should().Be(0.42);
    }
}
