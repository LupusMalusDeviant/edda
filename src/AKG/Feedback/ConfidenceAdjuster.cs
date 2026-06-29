using Edda.Core.Models;

namespace Edda.AKG.Feedback;

/// <summary>
/// Computes the confidence multiplier for a rule based on accumulated feedback events.
/// Uses a weighted combination of TDK pass rate, user sentiment, and compliance rate.
/// The resulting multiplier is clamped to [0.3, 1.3].
/// Requires a minimum number of total samples before adjustments are applied (cold-start protection).
/// </summary>
internal static class ConfidenceAdjuster
{
    // Weights for the three feedback sources (must sum to 1.0)
    private const double TdkWeight        = 0.5;
    private const double UserWeight       = 0.3;
    private const double ComplianceWeight = 0.2;

    // Minimum total samples before the multiplier deviates from neutral
    private const int MinSamplesRequired = 3;

    // Multiplier bounds and neutral point
    private const double MinMultiplier = 0.3;
    private const double MaxMultiplier = 1.3;
    private const double NeutralScore  = 1.0;

    /// <summary>
    /// Calculates the confidence multiplier for a rule given its feedback statistics.
    /// </summary>
    /// <param name="stats">Aggregated feedback data for the rule.</param>
    /// <returns>
    /// A multiplier in [<see cref="MinMultiplier"/>, <see cref="MaxMultiplier"/>].
    /// Returns 1.0 (neutral) when fewer than <see cref="MinSamplesRequired"/> total samples exist.
    /// </returns>
    internal static double Calculate(RuleFeedbackStats stats)
    {
        var totalSamples = stats.TdkPassCount + stats.TdkFailCount
                         + stats.UserPositiveCount + stats.UserNegativeCount
                         + stats.ComplianceCount + stats.NonComplianceCount;

        if (totalSamples < MinSamplesRequired)
            return NeutralScore;

        // TDK score: pass rate (0–1) → scaled to [Min, Max]
        var tdkScore = stats.TdkPassCount + stats.TdkFailCount > 0
            ? ScaleToRange(stats.TdkPassRate)
            : NeutralScore;

        // User score: positive / total user events → scaled to [Min, Max]
        var totalUser = stats.UserPositiveCount + stats.UserNegativeCount;
        var userScore = totalUser > 0
            ? ScaleToRange((double)stats.UserPositiveCount / totalUser)
            : NeutralScore;

        // Compliance score: compliant / total compliance checks → scaled to [Min, Max]
        var totalComp = stats.ComplianceCount + stats.NonComplianceCount;
        var compScore = totalComp > 0
            ? ScaleToRange((double)stats.ComplianceCount / totalComp)
            : NeutralScore;

        var combined = TdkWeight * tdkScore
                     + UserWeight * userScore
                     + ComplianceWeight * compScore;

        return Math.Clamp(combined, MinMultiplier, MaxMultiplier);
    }

    /// <summary>
    /// Scales a rate in [0, 1] to the multiplier range [<see cref="MinMultiplier"/>, <see cref="MaxMultiplier"/>].
    /// rate=1.0 → MaxMultiplier, rate=0.5 → NeutralScore, rate=0.0 → MinMultiplier.
    /// </summary>
    private static double ScaleToRange(double rate)
        => MinMultiplier + rate * (MaxMultiplier - MinMultiplier);
}
