using System.Collections.Concurrent;

namespace Edda.AKG.Confidence;

/// <summary>
/// Thread-safe in-memory sliding window for tracking rule confidence multipliers.
/// Multipliers are adjusted based on TDK feedback: successes increase and violations decrease the score.
/// Used by the TDK engine to weight rule selection in subsequent context compilations.
/// </summary>
public sealed class RuleConfidenceStore
{
    private const double DefaultMultiplier = 1.0;
    private const double MaxMultiplier = 2.0;
    private const double MinMultiplier = 0.1;
    private const double SuccessIncrement = 0.1;
    private const double ViolationDecrement = 0.2;

    private readonly ConcurrentDictionary<string, double> _multipliers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the current confidence multiplier for the specified rule.
    /// Returns <c>1.0</c> if no feedback has been recorded for this rule.
    /// </summary>
    /// <param name="ruleId">The rule ID to query.</param>
    /// <returns>Multiplier in the range [0.1, 2.0].</returns>
    public double GetMultiplier(string ruleId)
        => _multipliers.TryGetValue(ruleId, out var m) ? m : DefaultMultiplier;

    /// <summary>
    /// Records a successful rule application, increasing the multiplier by 0.1 (max 2.0).
    /// </summary>
    /// <param name="ruleId">The rule that was successfully applied.</param>
    public void RecordSuccess(string ruleId)
        => _multipliers.AddOrUpdate(
            ruleId,
            DefaultMultiplier + SuccessIncrement,
            static (_, current) => Math.Min(MaxMultiplier, current + SuccessIncrement));

    /// <summary>
    /// Records a TDK violation for a rule, decreasing the multiplier by 0.2 (min 0.1).
    /// </summary>
    /// <param name="ruleId">The rule that triggered a violation.</param>
    public void RecordViolation(string ruleId)
        => _multipliers.AddOrUpdate(
            ruleId,
            Math.Max(MinMultiplier, DefaultMultiplier - ViolationDecrement),
            static (_, current) => Math.Max(MinMultiplier, current - ViolationDecrement));

    /// <summary>
    /// Resets the confidence multiplier for the specified rule back to the default (1.0).
    /// </summary>
    /// <param name="ruleId">The rule ID to reset.</param>
    public void Reset(string ruleId) => _multipliers.TryRemove(ruleId, out _);
}
