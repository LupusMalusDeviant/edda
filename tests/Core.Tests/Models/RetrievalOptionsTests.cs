using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>
/// Unit tests for <see cref="RetrievalOptions"/>. The default instance must reproduce the historical
/// hard-coded retrieval thresholds so that unconfigured behaviour is unchanged.
/// </summary>
public class RetrievalOptionsTests
{
    [Fact]
    public void DefaultInstance_UsesHistoricalHardCodedValues()
    {
        var options = new RetrievalOptions();

        options.SimilarityThreshold.Should().Be(0.5);
        options.VectorTopK.Should().Be(100);
        options.MmrTopN.Should().Be(15);
        options.MmrLambda.Should().Be(0.7);
        options.HeadSimilarityThreshold.Should().Be(0.4);
    }
}
