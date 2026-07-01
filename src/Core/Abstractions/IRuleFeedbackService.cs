using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Records feedback events for AKG rules and adjusts their confidence scores
/// based on accumulated outcomes from TDK validations, user signals, and compliance checks.
/// </summary>
public interface IRuleFeedbackService
{
    /// <summary>
    /// Records the outcome of a TDK validation for a specific rule.
    /// Called by <see cref="ITdkEngine"/> after each validation cycle.
    /// </summary>
    /// <param name="ruleId">The ID of the rule that was validated.</param>
    /// <param name="passed">Whether the TDK validator reported a pass.</param>
    /// <param name="userId">Optional user ID for scoping (null = system-level).</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordTdkOutcomeAsync(
        string ruleId,
        bool passed,
        string? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records explicit user feedback for the current response.
    /// Propagates the signal to all rules that were active in the last context compilation
    /// for the given conversation.
    /// </summary>
    /// <param name="conversationId">The conversation the feedback applies to.</param>
    /// <param name="signal">The user's quality signal (positive, negative, or correction).</param>
    /// <param name="userId">The user providing the feedback.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordUserFeedbackAsync(
        string conversationId,
        FeedbackSignal signal,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Records whether the model's response actually followed a rule's guidance.
    /// Typically determined by an LLM-based compliance check run in the background.
    /// </summary>
    /// <param name="ruleId">The rule whose compliance is being checked.</param>
    /// <param name="conversationId">The conversation turn in which compliance was measured.</param>
    /// <param name="compliant">True if the response followed the rule's guidance.</param>
    /// <param name="userId">Optional user ID for scoping.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordComplianceAsync(
        string ruleId,
        string conversationId,
        bool compliant,
        string? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Increments the usage counter for a rule that was included in a context compilation.
    /// Fire-and-forget — callers should not await this; exceptions are silently suppressed.
    /// </summary>
    /// <param name="ruleId">The rule that was used.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordUsageAsync(string ruleId, CancellationToken ct = default);

    /// <summary>
    /// Returns the current feedback statistics for a rule.
    /// </summary>
    /// <param name="ruleId">The rule to query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="RuleFeedbackStats"/> snapshot for the rule.</returns>
    Task<RuleFeedbackStats> GetStatsAsync(string ruleId, CancellationToken ct = default);

    /// <summary>
    /// Returns feedback statistics for every rule that has accumulated feedback or usage. Rules with no
    /// recorded events are omitted (treated as neutral). Useful for coverage/health analysis such as
    /// finding low-confidence or stale rules.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Feedback statistics for all rules with recorded data.</returns>
    Task<IReadOnlyList<RuleFeedbackStats>> GetAllStatsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the confidence multipliers for a set of rules, keyed by rule ID.
    /// Used by the context compiler to adjust keyword scores during AKG compilation.
    /// Missing entries default to 1.0 (no adjustment).
    /// </summary>
    /// <param name="ruleIds">The rule IDs to look up.</param>
    /// <param name="userId">
    /// Optional user scope. When provided and the user has sufficient personal feedback for a rule,
    /// the global multiplier is blended with a user-specific overlay so that one user's feedback does
    /// not affect others. When <see langword="null"/>, the global (cross-user) multiplier is returned.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A dictionary mapping rule ID → confidence multiplier.</returns>
    Task<IReadOnlyDictionary<string, double>> GetMultipliersAsync(
        IEnumerable<string> ruleIds,
        string? userId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Triggers immediate recalculation of confidence scores for all rules.
    /// Normally runs as a daily background job (FeedbackSummaryJob).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RecalculateAllAsync(CancellationToken ct = default);
}
