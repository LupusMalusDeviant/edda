using Edda.Core.Benchmark;
using FluentAssertions;

namespace Edda.AKG.Tests.Benchmark;

public class RetrievalMetricsTests
{
    [Fact]
    public void Compute_PerfectRanking_AllMetricsMaximal()
    {
        var metrics = RetrievalMetrics.Compute(["A", "B", "C"], ["A", "B"], k: 3);

        metrics.RecallAtK.Should().Be(1.0);
        metrics.PrecisionAtK.Should().BeApproximately(2.0 / 3.0, 1e-9);
        metrics.Mrr.Should().Be(1.0);
        metrics.NdcgAtK.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Compute_NoRelevantInResults_AllMetricsZero()
    {
        var metrics = RetrievalMetrics.Compute(["X", "Y"], ["A"], k: 2);

        metrics.RecallAtK.Should().Be(0.0);
        metrics.PrecisionAtK.Should().Be(0.0);
        metrics.Mrr.Should().Be(0.0);
        metrics.NdcgAtK.Should().Be(0.0);
    }

    [Fact]
    public void Compute_RelevantBeyondCutoff_NotCounted()
    {
        // "C" is relevant but sits at position 3, beyond the k=2 cutoff.
        var metrics = RetrievalMetrics.Compute(["A", "B", "C"], ["C"], k: 2);

        metrics.RecallAtK.Should().Be(0.0);
        metrics.Mrr.Should().Be(0.0);
        metrics.NdcgAtK.Should().Be(0.0);
    }

    [Fact]
    public void Compute_FirstHitAtSecondPosition_MrrIsOneHalf()
    {
        var metrics = RetrievalMetrics.Compute(["A", "B"], ["B"], k: 2);

        metrics.RecallAtK.Should().Be(1.0);
        metrics.PrecisionAtK.Should().Be(0.5);
        metrics.Mrr.Should().Be(0.5);
        metrics.NdcgAtK.Should().BeApproximately(1.0 / Math.Log2(3), 1e-9);
    }

    [Fact]
    public void Compute_PartialRecall_NdcgBetweenZeroAndOne()
    {
        var metrics = RetrievalMetrics.Compute(["A", "X", "B"], ["A", "B", "C"], k: 3);

        metrics.RecallAtK.Should().BeApproximately(2.0 / 3.0, 1e-9);
        metrics.PrecisionAtK.Should().BeApproximately(2.0 / 3.0, 1e-9);
        metrics.Mrr.Should().Be(1.0);

        double dcg = (1.0 / Math.Log2(2)) + (1.0 / Math.Log2(4));            // A@1 + B@3
        double idcg = (1.0 / Math.Log2(2)) + (1.0 / Math.Log2(3)) + (1.0 / Math.Log2(4));
        metrics.NdcgAtK.Should().BeApproximately(dcg / idcg, 1e-9);
    }

    [Fact]
    public void Compute_EmptyExpected_AllMetricsZero()
    {
        var metrics = RetrievalMetrics.Compute(["A", "B"], [], k: 3);

        metrics.RecallAtK.Should().Be(0.0);
        metrics.PrecisionAtK.Should().Be(0.0);
        metrics.Mrr.Should().Be(0.0);
        metrics.NdcgAtK.Should().Be(0.0);
    }

    [Fact]
    public void Compute_NonPositiveK_AllMetricsZero()
    {
        var metrics = RetrievalMetrics.Compute(["A"], ["A"], k: 0);

        metrics.RecallAtK.Should().Be(0.0);
        metrics.PrecisionAtK.Should().Be(0.0);
    }

    [Theory]
    [InlineData(50, 30.0)]
    [InlineData(95, 50.0)]
    [InlineData(0, 10.0)]
    [InlineData(100, 50.0)]
    public void Percentile_NearestRank_ReturnsExpected(double percentile, double expected)
    {
        // n=5 → rank = ceil(percentile/100 * 5), clamped to [1, 5]; sorted = [10,20,30,40,50].
        double[] values = [50, 10, 40, 20, 30];

        RetrievalMetrics.Percentile(values, percentile).Should().Be(expected);
    }

    [Fact]
    public void Percentile_EmptyInput_ReturnsZero()
    {
        RetrievalMetrics.Percentile([], 50).Should().Be(0.0);
    }
}
