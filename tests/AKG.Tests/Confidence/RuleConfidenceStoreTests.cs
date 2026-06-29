using Edda.AKG.Confidence;

namespace Edda.AKG.Tests.Confidence;

public class RuleConfidenceStoreTests
{
    private readonly RuleConfidenceStore _store = new();

    [Fact]
    public void GetMultiplier_NewRule_ReturnsDefaultOne()
    {
        var result = _store.GetMultiplier("rule-001");

        result.Should().Be(1.0);
    }

    [Fact]
    public void RecordSuccess_IncreasesMultiplier()
    {
        _store.RecordSuccess("rule-001");

        var result = _store.GetMultiplier("rule-001");

        result.Should().BeApproximately(1.1, 0.0001);
    }

    [Fact]
    public void RecordSuccess_AtMax_StaysAtTwo()
    {
        for (var i = 0; i < 20; i++)
            _store.RecordSuccess("rule-max");

        var result = _store.GetMultiplier("rule-max");

        result.Should().Be(2.0);
    }

    [Fact]
    public void RecordViolation_DecreasesMultiplier()
    {
        _store.RecordViolation("rule-001");

        var result = _store.GetMultiplier("rule-001");

        result.Should().BeApproximately(0.8, 0.0001);
    }

    [Fact]
    public void RecordViolation_AtMin_StaysAtPointOne()
    {
        for (var i = 0; i < 20; i++)
            _store.RecordViolation("rule-min");

        var result = _store.GetMultiplier("rule-min");

        result.Should().Be(0.1);
    }

    [Fact]
    public void Reset_ResetsToDefault()
    {
        _store.RecordSuccess("rule-001");
        _store.RecordSuccess("rule-001");

        _store.Reset("rule-001");

        var result = _store.GetMultiplier("rule-001");
        result.Should().Be(1.0);
    }

    [Fact]
    public void ConcurrentAccess_ThreadSafe()
    {
        const string ruleId = "concurrent-rule";
        const int threadCount = 20;

        var threads = Enumerable.Range(0, threadCount)
            .Select(_ => new Thread(() => _store.RecordSuccess(ruleId)))
            .ToList();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        var result = _store.GetMultiplier(ruleId);
        result.Should().BeGreaterThan(1.0);
        result.Should().BeLessThanOrEqualTo(2.0);
    }
}
