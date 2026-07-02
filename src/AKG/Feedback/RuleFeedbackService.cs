using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Feedback;

/// <summary>
/// Production implementation of <see cref="IRuleFeedbackService"/>.
/// Persists feedback events to SQLite via <see cref="RuleFeedbackStore"/> and
/// exposes the latest confidence multipliers for use in the context compilation pipeline.
/// </summary>
internal sealed class RuleFeedbackService : IRuleFeedbackService
{
    // User-overlay tuning: minimum personal samples before a user's feedback influences the
    // multiplier, and the weight of the user overlay vs the global base when it does.
    private const int MinUserSamples = 3;
    private const double UserOverlayWeight = 0.6;
    private const double NeutralMultiplier = 1.0;

    private readonly IRuleFeedbackStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<RuleFeedbackService> _logger;
    private readonly double _decayHalfLifeDays;

    /// <summary>
    /// Initializes a new <see cref="RuleFeedbackService"/>.
    /// </summary>
    /// <param name="store">SQLite persistence layer for feedback events and stats.</param>
    /// <param name="time">Time provider for reproducible timestamps.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="decayHalfLifeDays">
    /// Confidence-decay half-life in days (see <see cref="ConfidenceAdjuster"/>). During recalculation,
    /// rules whose most recent feedback is older than this revert toward a neutral multiplier. Defaults to
    /// <see cref="ConfidenceAdjuster.DefaultDecayHalfLifeDays"/>; values &lt;= 0 disable decay.
    /// </param>
    internal RuleFeedbackService(
        IRuleFeedbackStore store,
        TimeProvider time,
        ILogger<RuleFeedbackService> logger,
        double decayHalfLifeDays = ConfidenceAdjuster.DefaultDecayHalfLifeDays)
    {
        _store             = store;
        _time              = time;
        _logger            = logger;
        _decayHalfLifeDays = decayHalfLifeDays;
    }

    /// <inheritdoc />
    public async Task RecordTdkOutcomeAsync(
        string ruleId, bool passed, string? userId = null, CancellationToken ct = default)
    {
        var evt = new RuleFeedbackEvent
        {
            EventId   = Guid.NewGuid().ToString(),
            RuleId    = ruleId,
            Type      = FeedbackEventType.TdkValidation,
            Positive  = passed,
            UserId    = userId,
            Timestamp = _time.GetUtcNow(),
        };

        await _store.AppendEventAsync(evt, ct).ConfigureAwait(false);

        _logger.LogDebug(
            "TDK outcome recorded: rule={RuleId} passed={Passed} | {Component}",
            ruleId, passed, "AKG.Feedback");
    }

    /// <inheritdoc />
    public async Task RecordUserFeedbackAsync(
        string conversationId, FeedbackSignal signal, string userId, CancellationToken ct = default)
    {
        var positive = signal == FeedbackSignal.Positive;

        // Propagate feedback to all rules active in this conversation
        var activeRules = await _store
            .GetActiveRulesForConversationAsync(conversationId, ct)
            .ConfigureAwait(false);

        foreach (var ruleId in activeRules)
        {
            var evt = new RuleFeedbackEvent
            {
                EventId        = Guid.NewGuid().ToString(),
                RuleId         = ruleId,
                Type           = FeedbackEventType.UserFeedback,
                Positive       = positive,
                UserId         = userId,
                ConversationId = conversationId,
                Timestamp      = _time.GetUtcNow(),
            };
            await _store.AppendEventAsync(evt, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "User feedback recorded: conversationId={ConvId} signal={Signal} propagatedRules={Count} | {Component}",
            conversationId, signal, activeRules.Count, "AKG.Feedback");
    }

    /// <inheritdoc />
    public async Task RecordRuleRatingAsync(
        string ruleId, RuleRating rating, string userId, CancellationToken ct = default)
    {
        var evt = new RuleFeedbackEvent
        {
            EventId   = Guid.NewGuid().ToString(),
            RuleId    = ruleId,
            Type      = FeedbackEventType.UserFeedback,
            Positive  = rating == RuleRating.Helpful,
            UserId    = userId,
            Timestamp = _time.GetUtcNow(),
        };

        await _store.AppendEventAsync(evt, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Rule rating recorded: rule={RuleId} rating={Rating} userId={UserId} | {Component}",
            ruleId, rating, userId, "AKG.Feedback");
    }

    /// <inheritdoc />
    public async Task RecordComplianceAsync(
        string ruleId, string conversationId, bool compliant,
        string? userId = null, CancellationToken ct = default)
    {
        var evt = new RuleFeedbackEvent
        {
            EventId        = Guid.NewGuid().ToString(),
            RuleId         = ruleId,
            Type           = FeedbackEventType.ComplianceCheck,
            Positive       = compliant,
            UserId         = userId,
            ConversationId = conversationId,
            Timestamp      = _time.GetUtcNow(),
        };

        await _store.AppendEventAsync(evt, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(string ruleId, CancellationToken ct = default)
    {
        try
        {
            await _store.IncrementUsageAsync(ruleId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Usage tracking is fire-and-forget; never propagate exceptions to callers
            _logger.LogDebug(ex,
                "Usage recording failed silently for rule {RuleId} | {Component}",
                ruleId, "AKG.Feedback");
        }
    }

    /// <inheritdoc />
    public async Task<RuleFeedbackStats> GetStatsAsync(string ruleId, CancellationToken ct = default)
        => await _store.GetStatsForRuleAsync(ruleId, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RuleFeedbackStats>> GetAllStatsAsync(CancellationToken ct = default)
        => await _store.GetAllStatsAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, double>> GetMultipliersAsync(
        IEnumerable<string> ruleIds, string? userId = null, CancellationToken ct = default)
    {
        var ids = ruleIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return new Dictionary<string, double>();

        var allStats = await _store.GetAllStatsAsync(ct).ConfigureAwait(false);
        var result = allStats
            .Where(s => ids.Contains(s.RuleId))
            .ToDictionary(s => s.RuleId, s => s.ConfidenceMultiplier, StringComparer.Ordinal);

        // No user scope → global (cross-user) multipliers only.
        if (string.IsNullOrEmpty(userId))
            return result;

        // User overlay: where the user has enough personal feedback for a rule, blend the global
        // base with a user-specific multiplier so one user's feedback does not affect others.
        // Below the sample threshold the global value is kept unchanged.
        var userStats = await _store.GetUserStatsAsync(userId, ct).ConfigureAwait(false);
        foreach (var us in userStats)
        {
            if (!ids.Contains(us.RuleId) || TotalSamples(us) < MinUserSamples)
                continue;

            var userMultiplier = ConfidenceAdjuster.Calculate(us);
            var globalMultiplier = result.TryGetValue(us.RuleId, out var g) ? g : NeutralMultiplier;
            result[us.RuleId] = UserOverlayWeight * userMultiplier
                              + (1.0 - UserOverlayWeight) * globalMultiplier;
        }

        return result;
    }

    /// <summary>Total feedback samples across all sources for a rule's statistics.</summary>
    private static int TotalSamples(RuleFeedbackStats s)
        => s.TdkPassCount + s.TdkFailCount
         + s.UserPositiveCount + s.UserNegativeCount
         + s.ComplianceCount + s.NonComplianceCount;

    /// <inheritdoc />
    public async Task RecalculateAllAsync(CancellationToken ct = default)
    {
        var allStats = await _store.GetAllStatsAsync(ct).ConfigureAwait(false);
        var now = _time.GetUtcNow();

        var boosted  = 0;
        var degraded = 0;
        var unchanged = 0;

        foreach (var stats in allStats)
        {
            var newMultiplier = ConfidenceAdjuster.Calculate(stats, now, _decayHalfLifeDays);

            await _store.UpdateMultiplierAsync(stats.RuleId, newMultiplier, now, ct)
                .ConfigureAwait(false);

            if (Math.Abs(newMultiplier - stats.ConfidenceMultiplier) < 0.001)
                unchanged++;
            else if (newMultiplier > stats.ConfidenceMultiplier)
                boosted++;
            else
                degraded++;
        }

        _logger.LogInformation(
            "Confidence recalculation: {Total} rules processed. Boosted={Boosted} Degraded={Degraded} Unchanged={Unchanged} | {Component}",
            allStats.Count, boosted, degraded, unchanged, "AKG.Feedback");
    }
}
