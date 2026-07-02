using Edda.Core.Resilience;

namespace Edda.Core.Tests.Resilience;

/// <summary>
/// Unit tests for <see cref="ExponentialBackoff"/>: exponential growth of the per-attempt delay,
/// saturation at the cap, proportional jitter, and argument validation.
/// </summary>
public class ExponentialBackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(100);

    [Fact]
    public void ComputeDelay_IncreasingAttempts_DelaysGrowExponentially()
    {
        ExponentialBackoff.ComputeDelay(0, Base, Max).Should().Be(TimeSpan.FromSeconds(1));
        ExponentialBackoff.ComputeDelay(1, Base, Max).Should().Be(TimeSpan.FromSeconds(2));
        ExponentialBackoff.ComputeDelay(2, Base, Max).Should().Be(TimeSpan.FromSeconds(4));
        ExponentialBackoff.ComputeDelay(3, Base, Max).Should().Be(TimeSpan.FromSeconds(8));
        ExponentialBackoff.ComputeDelay(4, Base, Max).Should().Be(TimeSpan.FromSeconds(16));
    }

    [Fact]
    public void ComputeDelay_EachAttempt_IsDoubleThePrevious()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var current = ExponentialBackoff.ComputeDelay(attempt, Base, Max);
            var next = ExponentialBackoff.ComputeDelay(attempt + 1, Base, Max);
            next.Should().Be(current * 2, because: "each retry waits twice as long as the previous one until capped");
        }
    }

    [Fact]
    public void ComputeDelay_AttemptExceedingCap_ClampsToMaxDelay()
    {
        // 1s * 2^7 = 128s, which exceeds the 100s cap.
        ExponentialBackoff.ComputeDelay(7, Base, Max).Should().Be(Max);
        ExponentialBackoff.ComputeDelay(30, Base, Max).Should().Be(Max);
    }

    [Fact]
    public void ComputeDelay_ZeroBaseDelay_ReturnsZeroForAnyAttempt()
    {
        ExponentialBackoff.ComputeDelay(0, TimeSpan.Zero, Max).Should().Be(TimeSpan.Zero);
        ExponentialBackoff.ComputeDelay(5, TimeSpan.Zero, Max).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ComputeDelay_NegativeAttempt_Throws()
    {
        var act = () => ExponentialBackoff.ComputeDelay(-1, Base, Max);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ComputeDelay_NegativeBaseDelay_Throws()
    {
        var act = () => ExponentialBackoff.ComputeDelay(0, TimeSpan.FromSeconds(-1), Max);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ComputeDelay_MaxDelayLessThanBaseDelay_Throws()
    {
        var act = () => ExponentialBackoff.ComputeDelay(0, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithJitter_ZeroSample_ReturnsDelayUnchanged()
    {
        var delay = TimeSpan.FromSeconds(4);
        ExponentialBackoff.WithJitter(delay, 0.5, 0.0).Should().Be(delay);
    }

    [Fact]
    public void WithJitter_ZeroFraction_ReturnsDelayUnchanged()
    {
        var delay = TimeSpan.FromSeconds(4);
        ExponentialBackoff.WithJitter(delay, 0.0, 1.0).Should().Be(delay);
    }

    [Fact]
    public void WithJitter_FullSample_AddsFullFraction()
    {
        var delay = TimeSpan.FromSeconds(10);
        // 10s + 10s * 0.2 * 1.0 = 12s
        ExponentialBackoff.WithJitter(delay, 0.2, 1.0).Should().Be(TimeSpan.FromSeconds(12));
    }

    [Fact]
    public void WithJitter_HalfSample_AddsHalfFraction()
    {
        var delay = TimeSpan.FromSeconds(10);
        // 10s + 10s * 0.2 * 0.5 = 11s
        ExponentialBackoff.WithJitter(delay, 0.2, 0.5).Should().Be(TimeSpan.FromSeconds(11));
    }

    [Fact]
    public void WithJitter_GrowingSample_ProducesMonotonicallyLargerDelays()
    {
        var delay = TimeSpan.FromSeconds(10);
        var low = ExponentialBackoff.WithJitter(delay, 0.5, 0.1);
        var mid = ExponentialBackoff.WithJitter(delay, 0.5, 0.5);
        var high = ExponentialBackoff.WithJitter(delay, 0.5, 0.9);

        low.Should().BeGreaterThanOrEqualTo(delay);
        mid.Should().BeGreaterThan(low);
        high.Should().BeGreaterThan(mid);
    }

    [Fact]
    public void WithJitter_NegativeFraction_Throws()
    {
        var act = () => ExponentialBackoff.WithJitter(TimeSpan.FromSeconds(1), -0.1, 0.5);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithJitter_NegativeSample_Throws()
    {
        var act = () => ExponentialBackoff.WithJitter(TimeSpan.FromSeconds(1), 0.2, -0.5);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
