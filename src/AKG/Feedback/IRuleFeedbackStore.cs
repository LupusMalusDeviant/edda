using Edda.Core.Models;

namespace Edda.AKG.Feedback;

/// <summary>
/// SQLite-backed persistence for rule feedback events and the derived confidence statistics
/// used by the context-compilation pipeline (F32 — rule feedback loop).
/// </summary>
internal interface IRuleFeedbackStore
{
    /// <summary>Appends a feedback event to the durable event log.</summary>
    /// <param name="evt">The feedback event to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the event is stored.</returns>
    Task AppendEventAsync(RuleFeedbackEvent evt, CancellationToken ct);

    /// <summary>Increments the usage counter for a rule.</summary>
    /// <param name="ruleId">The id of the rule that was used.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the counter is updated.</returns>
    Task IncrementUsageAsync(string ruleId, CancellationToken ct);

    /// <summary>Persists a recalculated confidence multiplier for a rule.</summary>
    /// <param name="ruleId">The id of the rule.</param>
    /// <param name="multiplier">The new confidence multiplier.</param>
    /// <param name="recalculatedAt">The timestamp of the recalculation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the multiplier is stored.</returns>
    Task UpdateMultiplierAsync(string ruleId, double multiplier, DateTimeOffset recalculatedAt, CancellationToken ct);

    /// <summary>Returns the aggregated feedback statistics for every rule.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The global feedback statistics per rule.</returns>
    Task<IReadOnlyList<RuleFeedbackStats>> GetAllStatsAsync(CancellationToken ct);

    /// <summary>Returns the per-rule feedback statistics scoped to a single user.</summary>
    /// <param name="userId">The user whose feedback overlay to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The user-scoped feedback statistics per rule.</returns>
    Task<IReadOnlyList<RuleFeedbackStats>> GetUserStatsAsync(string userId, CancellationToken ct);

    /// <summary>Returns the aggregated feedback statistics for a single rule.</summary>
    /// <param name="ruleId">The id of the rule.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The feedback statistics for the rule.</returns>
    Task<RuleFeedbackStats> GetStatsForRuleAsync(string ruleId, CancellationToken ct);

    /// <summary>Returns the distinct rule ids that were active in a conversation.</summary>
    /// <param name="conversationId">The conversation to inspect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The rule ids referenced by events in the conversation.</returns>
    Task<IReadOnlyList<string>> GetActiveRulesForConversationAsync(string conversationId, CancellationToken ct);
}
