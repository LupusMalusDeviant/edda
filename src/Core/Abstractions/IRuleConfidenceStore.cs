namespace Edda.Core.Abstractions;

/// <summary>
/// Tracks rule validation outcomes and computes a confidence multiplier
/// used by the context compiler to adjust rule weights dynamically.
/// The confidence multiplier decreases for rules that frequently produce violations.
/// </summary>
public interface IRuleConfidenceStore
{
    /// <summary>
    /// Records whether a TDK validator run passed or failed for the given rule.
    /// Maintains a sliding window of the last 20 outcomes.
    /// </summary>
    /// <param name="ruleId">The AKG rule identifier.</param>
    /// <param name="passed">
    /// <see langword="true"/> when the validator found no violations;
    /// <see langword="false"/> when violations were detected or the validator crashed.
    /// </param>
    void RecordOutcome(string ruleId, bool passed);

    /// <summary>
    /// Returns the confidence multiplier for a rule based on its validation history.
    /// Formula: <c>0.3 + (passRate × 0.7)</c>, where passRate is calculated over the sliding window.
    /// Returns <c>1.0</c> when no outcomes have been recorded.
    /// </summary>
    /// <param name="ruleId">The AKG rule identifier.</param>
    /// <returns>A multiplier in the range [0.3, 1.0].</returns>
    double GetMultiplier(string ruleId);
}
