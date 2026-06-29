using Edda.AKG.Confidence;

namespace Edda.AKG.Tests.Confidence;

public class SlidingWindowRuleConfidenceStoreTests
{
    private readonly SlidingWindowRuleConfidenceStore _store = new();

    [Fact]
    public void GetMultiplier_NewRule_ReturnsOne()
    {
        var result = _store.GetMultiplier("unknown-rule");

        result.Should().Be(1.0);
    }

    [Fact]
    public void RuleConfidenceStore_AllPassing_MultiplierIsOne()
    {
        for (var i = 0; i < 10; i++)
            _store.RecordOutcome("r1", passed: true);

        var result = _store.GetMultiplier("r1");

        result.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void RuleConfidenceStore_AllFailing_MultiplierIsPointThree()
    {
        for (var i = 0; i < 10; i++)
            _store.RecordOutcome("r1", passed: false);

        var result = _store.GetMultiplier("r1");

        result.Should().BeApproximately(0.3, 0.0001);
    }

    [Fact]
    public void RecordOutcome_HalfPassingHalfFailing_MultiplierIsPointSixFive()
    {
        for (var i = 0; i < 5; i++)
            _store.RecordOutcome("r1", passed: true);
        for (var i = 0; i < 5; i++)
            _store.RecordOutcome("r1", passed: false);

        var result = _store.GetMultiplier("r1");

        // 0.3 + (0.5 × 0.7) = 0.3 + 0.35 = 0.65
        result.Should().BeApproximately(0.65, 0.0001);
    }

    [Fact]
    public void RecordOutcome_SlidingWindow_EvictsOldestEntry()
    {
        // Fill window with failures (20 entries)
        for (var i = 0; i < 20; i++)
            _store.RecordOutcome("r1", passed: false);

        // Overwrite all with passes
        for (var i = 0; i < 20; i++)
            _store.RecordOutcome("r1", passed: true);

        var result = _store.GetMultiplier("r1");

        result.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void RecordOutcome_WindowSizeIs20_OnlyLast20Counted()
    {
        // 10 failures, then 20 passes → window should only contain the 20 passes
        for (var i = 0; i < 10; i++)
            _store.RecordOutcome("r1", passed: false);
        for (var i = 0; i < 20; i++)
            _store.RecordOutcome("r1", passed: true);

        var result = _store.GetMultiplier("r1");

        result.Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void MultiplierRange_NeverBelowPointThree()
    {
        for (var i = 0; i < 100; i++)
            _store.RecordOutcome("r1", passed: false);

        var result = _store.GetMultiplier("r1");

        result.Should().BeGreaterThanOrEqualTo(0.3);
    }

    [Fact]
    public void MultiplierRange_NeverAboveOne()
    {
        for (var i = 0; i < 100; i++)
            _store.RecordOutcome("r1", passed: true);

        var result = _store.GetMultiplier("r1");

        result.Should().BeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void RecordOutcome_MultipleRules_AreIsolated()
    {
        for (var i = 0; i < 5; i++)
            _store.RecordOutcome("rule-a", passed: true);
        for (var i = 0; i < 5; i++)
            _store.RecordOutcome("rule-b", passed: false);

        _store.GetMultiplier("rule-a").Should().BeApproximately(1.0, 0.0001);
        _store.GetMultiplier("rule-b").Should().BeApproximately(0.3, 0.0001);
    }
}
