using Edda.AKG.Feedback;
using Edda.Core.Models;
using FluentAssertions;

namespace Edda.AKG.Tests.Feedback;

/// <summary>
/// Unit tests for <see cref="ConfidenceAdjuster"/>.
/// All tests use reflection-friendly internal access via InternalsVisibleTo.
/// </summary>
public sealed class ConfidenceAdjusterTests
{
    // ── Helper ─────────────────────────────────────────────────────────────────

    private static RuleFeedbackStats Stats(
        int tdkPass = 0, int tdkFail = 0,
        int userPos = 0, int userNeg = 0,
        int comp    = 0, int nonComp = 0) =>
        new()
        {
            RuleId             = "test-rule",
            TdkPassCount       = tdkPass,
            TdkFailCount       = tdkFail,
            UserPositiveCount  = userPos,
            UserNegativeCount  = userNeg,
            ComplianceCount    = comp,
            NonComplianceCount = nonComp,
        };

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_InsufficientSamples_ReturnsNeutral()
    {
        // 2 samples < MinSamplesRequired (3)
        var stats = Stats(tdkPass: 1, tdkFail: 1);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().Be(1.0);
    }

    [Fact]
    public void Calculate_AllTdkPass_BoostsMultiplierAboveNeutral()
    {
        // Only TDK data: 100% pass → TDK score=1.3, User=1.0 (neutral), Comp=1.0 (neutral)
        // Combined = 0.5*1.3 + 0.3*1.0 + 0.2*1.0 = 1.15
        var stats = Stats(tdkPass: 10);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().BeApproximately(1.15, precision: 0.01);
    }

    [Fact]
    public void Calculate_AllTdkFail_DegradedButNotMin()
    {
        // Only TDK data: 0% pass → TDK score=0.3, User=1.0 (neutral), Comp=1.0 (neutral)
        // Combined = 0.5*0.3 + 0.3*1.0 + 0.2*1.0 = 0.65
        var stats = Stats(tdkFail: 10);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().BeApproximately(0.65, precision: 0.01);
    }

    [Fact]
    public void Calculate_AllSourcesAtMax_ReturnsMaxMultiplier()
    {
        // TDK 100% pass, User 100% positive, Compliance 100% compliant
        // TDK=1.3, User=1.3, Comp=1.3 → combined = 1.3
        var stats = Stats(tdkPass: 10, userPos: 10, comp: 10);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().BeApproximately(1.3, precision: 0.01);
    }

    [Fact]
    public void Calculate_AllSourcesAtMin_ReturnsMinMultiplier()
    {
        // TDK 0%, User 0%, Compliance 0% → combined = 0.3
        var stats = Stats(tdkFail: 10, userNeg: 10, nonComp: 10);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().BeApproximately(0.3, precision: 0.01);
    }

    [Fact]
    public void Calculate_MultiplierClampedToRange()
    {
        // Extreme mixed signals should still be in [0.3, 1.3]
        var stats = Stats(tdkPass: 100, tdkFail: 0, userNeg: 100, nonComp: 100);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().BeGreaterThanOrEqualTo(0.3);
        result.Should().BeLessThanOrEqualTo(1.3);
    }

    [Fact]
    public void Calculate_MixedSignals_ReturnsWeightedCombination()
    {
        // TDK 100% pass, User 50% positive, Compliance 100% compliant
        // Expected: TDK=1.3, User=0.8 (50% → MinMultiplier + 0.5*(1.0) = 0.3+0.5=0.8), Comp=1.3
        // Combined: 0.5*1.3 + 0.3*0.8 + 0.2*1.3 = 0.65 + 0.24 + 0.26 = 1.15
        var stats = Stats(tdkPass: 10, userPos: 5, userNeg: 5, comp: 10);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().BeApproximately(1.15, precision: 0.01);
    }

    [Fact]
    public void Calculate_AllNeutral_ReturnsNeutral()
    {
        // No TDK/User/Compliance data → all scores neutral (1.0)
        // Only usage count provided — combined = 0.5*1.0 + 0.3*1.0 + 0.2*1.0 = 1.0
        // But we need >= MinSamples, so add 3 usage-counted events
        // Actually usage events don't count as samples — need TDK/User/Compliance
        // → stays at neutral (insufficient samples)
        var stats = new RuleFeedbackStats
        {
            RuleId     = "test-rule",
            UsageCount = 100,    // usage count alone is not a "sample"
        };

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().Be(1.0);
    }

    [Fact]
    public void Calculate_OnlyUserFeedback_AllPositive_BoostsMultiplier()
    {
        // 5 positive user samples (>= MinSamples=3)
        // TDK=1.0 (neutral, no data), User=1.3 (100% positive), Comp=1.0 (neutral)
        // Combined: 0.5*1.0 + 0.3*1.3 + 0.2*1.0 = 0.5 + 0.39 + 0.2 = 1.09
        var stats = Stats(userPos: 5);

        var result = ConfidenceAdjuster.Calculate(stats);

        result.Should().BeApproximately(1.09, precision: 0.01);
    }
}
