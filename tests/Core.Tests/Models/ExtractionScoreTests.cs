using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>Unit tests for <see cref="ExtractionScore"/> precision/recall/F1 computation.</summary>
public class ExtractionScoreTests
{
    [Fact]
    public void Compute_PerfectMatch_ScoresOne()
    {
        var score = ExtractionScore.Compute(truePositives: 3, predictedCount: 3, goldenCount: 3);

        score.Precision.Should().Be(1.0);
        score.Recall.Should().Be(1.0);
        score.F1.Should().Be(1.0);
    }

    [Fact]
    public void Compute_Partial_ScoresPrecisionRecallF1()
    {
        // predicted 4, golden 2, 1 correct → P=0.25, R=0.5, F1=2*0.25*0.5/0.75.
        var score = ExtractionScore.Compute(truePositives: 1, predictedCount: 4, goldenCount: 2);

        score.Precision.Should().Be(0.25);
        score.Recall.Should().Be(0.5);
        score.F1.Should().BeApproximately(1.0 / 3.0, 1e-9);
    }

    [Fact]
    public void Compute_BothEmpty_ScoresOne()
        => ExtractionScore.Compute(0, 0, 0).F1.Should().Be(1.0);

    [Fact]
    public void Compute_NothingPredictedButGoldenExists_ScoresZero()
    {
        var score = ExtractionScore.Compute(truePositives: 0, predictedCount: 0, goldenCount: 3);

        score.Precision.Should().Be(0.0);
        score.Recall.Should().Be(0.0);
        score.F1.Should().Be(0.0);
    }

    [Fact]
    public void Mean_AveragesEachComponent()
    {
        var mean = ExtractionScore.Mean(
        [
            new ExtractionScore { Precision = 1.0, Recall = 0.5, F1 = 0.6 },
            new ExtractionScore { Precision = 0.0, Recall = 0.5, F1 = 0.4 },
        ]);

        mean.Precision.Should().Be(0.5);
        mean.Recall.Should().Be(0.5);
        mean.F1.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Mean_Empty_ScoresZero()
        => ExtractionScore.Mean([]).F1.Should().Be(0.0);
}
