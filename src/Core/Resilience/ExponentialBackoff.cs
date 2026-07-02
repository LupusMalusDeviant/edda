namespace Edda.Core.Resilience;

/// <summary>
/// Pure, dependency-free helpers for computing exponential-backoff retry delays with optional jitter.
/// Deliberately side-effect-free (no clock access, no sleeping): callers drive the actual waiting through
/// an injected <see cref="System.TimeProvider"/>, which keeps the delay progression deterministic and
/// unit-testable in isolation.
/// </summary>
public static class ExponentialBackoff
{
    /// <summary>
    /// Computes the backoff delay for a zero-based retry <paramref name="attempt"/> as
    /// <c>baseDelay * 2^attempt</c>, clamped to <paramref name="maxDelay"/>. The delay therefore grows
    /// exponentially with each attempt until it saturates at the cap.
    /// </summary>
    /// <param name="attempt">Zero-based attempt index (0 for the first retry, 1 for the second, and so on).</param>
    /// <param name="baseDelay">Delay before the first retry (attempt 0). Must be non-negative.</param>
    /// <param name="maxDelay">Upper bound the delay is clamped to. Must be &gt;= <paramref name="baseDelay"/>.</param>
    /// <returns>The exponentially-scaled delay, never exceeding <paramref name="maxDelay"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="attempt"/> is negative, <paramref name="baseDelay"/> is negative, or
    /// <paramref name="maxDelay"/> is less than <paramref name="baseDelay"/>.
    /// </exception>
    public static TimeSpan ComputeDelay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        ArgumentOutOfRangeException.ThrowIfNegative(baseDelay.Ticks);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay.Ticks, baseDelay.Ticks);

        var maxTicks = maxDelay.Ticks;
        var ticks = baseDelay.Ticks;

        // Double per attempt, saturating at the cap. The "> maxTicks / 2" guard both stops the doubling once
        // it would reach the cap and prevents long overflow (ticks * 2 is only evaluated while ticks <= max/2).
        for (var i = 0; i < attempt && ticks < maxTicks; i++)
            ticks = ticks > maxTicks / 2 ? maxTicks : ticks * 2;

        return TimeSpan.FromTicks(Math.Min(ticks, maxTicks));
    }

    /// <summary>
    /// Applies proportional jitter to a delay: returns <c>delay + delay * jitterFraction * sample</c>.
    /// Spreading retries by a random fraction avoids a thundering herd when many callers back off in
    /// lockstep. The randomness is supplied as <paramref name="sample"/> so this function stays pure and
    /// unit-testable; production callers pass e.g. <see cref="System.Random.NextDouble()"/>.
    /// </summary>
    /// <param name="delay">The base (already exponentially-scaled) delay.</param>
    /// <param name="jitterFraction">Maximum added fraction (e.g. <c>0.2</c> = up to +20%). Must be non-negative.</param>
    /// <param name="sample">A value in <c>[0, 1)</c> selecting how much of the jitter fraction to apply. Must be non-negative.</param>
    /// <returns>The delay with jitter added; equal to <paramref name="delay"/> when either factor is zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="jitterFraction"/> or <paramref name="sample"/> is negative.
    /// </exception>
    public static TimeSpan WithJitter(TimeSpan delay, double jitterFraction, double sample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(jitterFraction);
        ArgumentOutOfRangeException.ThrowIfNegative(sample);

        var extraTicks = (long)(delay.Ticks * jitterFraction * sample);
        return delay + TimeSpan.FromTicks(extraTicks);
    }
}
