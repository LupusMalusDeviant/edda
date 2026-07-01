namespace Edda.Core.Models;

/// <summary>
/// User-provided quality signal for the agent's response.
/// </summary>
public enum FeedbackSignal
{
    /// <summary>User explicitly approved the response (thumbs up).</summary>
    Positive,

    /// <summary>User explicitly rejected the response (thumbs down).</summary>
    Negative,

    /// <summary>User corrected the response — implies negative signal.</summary>
    Correction,
}

/// <summary>
/// Discriminates the source of a feedback event recorded for an AKG rule.
/// </summary>
public enum FeedbackEventType
{
    /// <summary>Feedback originated from a TDK validation pass or failure.</summary>
    TdkValidation,

    /// <summary>Feedback originated from explicit user action (thumbs up/down).</summary>
    UserFeedback,

    /// <summary>Feedback originated from an automated LLM compliance check.</summary>
    ComplianceCheck,
}

/// <summary>
/// Aggregated feedback statistics for a single AKG rule.
/// Used by the ConfidenceAdjuster to compute the confidence multiplier.
/// </summary>
public sealed record RuleFeedbackStats
{
    /// <summary>The ID of the AKG rule these statistics belong to.</summary>
    public required string RuleId { get; init; }

    /// <summary>Number of TDK validation passes for this rule.</summary>
    public int TdkPassCount { get; init; }

    /// <summary>Number of TDK validation failures for this rule.</summary>
    public int TdkFailCount { get; init; }

    /// <summary>
    /// TDK pass rate: <see cref="TdkPassCount"/> / total TDK samples.
    /// Returns 1.0 when no TDK samples exist (neutral default).
    /// </summary>
    public double TdkPassRate => TdkPassCount + TdkFailCount == 0
        ? 1.0
        : (double)TdkPassCount / (TdkPassCount + TdkFailCount);

    /// <summary>Number of positive user feedback events that included this rule.</summary>
    public int UserPositiveCount { get; init; }

    /// <summary>Number of negative or correction user feedback events that included this rule.</summary>
    public int UserNegativeCount { get; init; }

    /// <summary>Number of compliance checks where the model followed this rule's guidance.</summary>
    public int ComplianceCount { get; init; }

    /// <summary>Number of compliance checks where the model ignored this rule's guidance.</summary>
    public int NonComplianceCount { get; init; }

    /// <summary>Total times this rule was included in a context compilation.</summary>
    public int UsageCount { get; init; }

    /// <summary>
    /// Calculated confidence adjustment factor in [0.3, 1.3].
    /// Applied multiplicatively to the rule's keyword score during context compilation.
    /// Values above 1.0 boost frequently-validated rules; below 1.0 degrade poor performers.
    /// Defaults to 1.0 (neutral) until sufficient data is accumulated.
    /// </summary>
    public double ConfidenceMultiplier { get; init; } = 1.0;

    /// <summary>When the confidence multiplier was last recalculated by the background job.</summary>
    public DateTimeOffset? LastRecalculated { get; init; }

    /// <summary>
    /// Timestamp of the most recent feedback event recorded for this rule, or null when the rule has no
    /// events yet. Drives confidence decay: the older the last reinforcement, the more the multiplier
    /// reverts toward neutral (1.0).
    /// </summary>
    public DateTimeOffset? LastFeedbackAt { get; init; }
}

/// <summary>
/// A single feedback event recorded for an AKG rule.
/// Persisted to SQLite via the RuleFeedbackStore.
/// </summary>
public sealed record RuleFeedbackEvent
{
    /// <summary>Unique event identifier (GUID).</summary>
    public required string EventId { get; init; }

    /// <summary>The AKG rule this event applies to.</summary>
    public required string RuleId { get; init; }

    /// <summary>The source of the feedback.</summary>
    public required FeedbackEventType Type { get; init; }

    /// <summary>True for positive outcomes (TDK pass, thumbs-up, compliance); false for negative.</summary>
    public required bool Positive { get; init; }

    /// <summary>Optional user ID (null for system-level events).</summary>
    public string? UserId { get; init; }

    /// <summary>Optional conversation ID linking the feedback to a specific turn.</summary>
    public string? ConversationId { get; init; }

    /// <summary>When this event was recorded.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}
