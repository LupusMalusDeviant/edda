using Edda.AKG.Feedback;
using Edda.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Edda.AKG.Tests.Feedback;

/// <summary>
/// Unit tests for <see cref="RuleFeedbackService"/>.
/// Uses an in-memory SQLite database (:memory:) for isolation.
/// </summary>
public sealed class RuleFeedbackServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly RuleFeedbackStore _store;
    private readonly RuleFeedbackService _sut;

    public RuleFeedbackServiceTests()
    {
        // Use a temp file so SQLite WAL mode is supported (not all features work with :memory:)
        _dbPath = Path.GetTempFileName() + ".db";
        _store  = new RuleFeedbackStore(_dbPath, NullLogger<RuleFeedbackStore>.Instance);
        _sut    = new RuleFeedbackService(
            _store,
            TimeProvider.System,
            NullLogger<RuleFeedbackService>.Instance);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }

    // ── TDK outcome recording ─────────────────────────────────────────────────

    [Fact]
    public async Task RecordTdkOutcome_Pass_UpdatesTdkPassCount()
    {
        await _sut.RecordTdkOutcomeAsync("rule-1", passed: true);

        var stats = await _sut.GetStatsAsync("rule-1");

        stats.TdkPassCount.Should().Be(1);
        stats.TdkFailCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordTdkOutcome_Fail_UpdatesTdkFailCount()
    {
        await _sut.RecordTdkOutcomeAsync("rule-1", passed: false);

        var stats = await _sut.GetStatsAsync("rule-1");

        stats.TdkFailCount.Should().Be(1);
        stats.TdkPassCount.Should().Be(0);
    }

    // ── User feedback ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordUserFeedback_PropagatesActiveRules()
    {
        // Seed: rule-A was "active" in conv-1 by recording a usage event tied to it
        // We do this via a TDK outcome with conversationId — but RecordUsageAsync does not
        // store conversationId. To have GetActiveRulesForConversationAsync return something
        // we need an event with conversationId. Let's record a TDK outcome approach instead:
        // The simpler approach: record a compliance event with conversationId for rule-A
        await _sut.RecordComplianceAsync("rule-A", "conv-1", compliant: true);

        // Now record user feedback for conv-1
        await _sut.RecordUserFeedbackAsync("conv-1", FeedbackSignal.Positive, userId: "user-1");

        var stats = await _sut.GetStatsAsync("rule-A");

        stats.UserPositiveCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordUserFeedback_NegativeSignal_UpdatesNegativeCount()
    {
        await _sut.RecordComplianceAsync("rule-B", "conv-2", compliant: false);
        await _sut.RecordUserFeedbackAsync("conv-2", FeedbackSignal.Negative, userId: "user-1");

        var stats = await _sut.GetStatsAsync("rule-B");

        stats.UserNegativeCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordUserFeedback_CorrectionSignal_CountsAsNegative()
    {
        await _sut.RecordComplianceAsync("rule-C", "conv-3", compliant: true);
        await _sut.RecordUserFeedbackAsync("conv-3", FeedbackSignal.Correction, userId: "user-1");

        var stats = await _sut.GetStatsAsync("rule-C");

        // Correction → Positive=false → UserNegativeCount
        stats.UserNegativeCount.Should().Be(1);
        stats.UserPositiveCount.Should().Be(0);
    }

    // ── Compliance recording ───────────────────────────────────────────────────

    [Fact]
    public async Task RecordCompliance_Compliant_UpdatesComplianceCount()
    {
        await _sut.RecordComplianceAsync("rule-D", "conv-4", compliant: true);

        var stats = await _sut.GetStatsAsync("rule-D");

        stats.ComplianceCount.Should().Be(1);
        stats.NonComplianceCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordCompliance_NonCompliant_UpdatesNonComplianceCount()
    {
        await _sut.RecordComplianceAsync("rule-D", "conv-5", compliant: false);

        var stats = await _sut.GetStatsAsync("rule-D");

        stats.NonComplianceCount.Should().Be(1);
    }

    // ── Multiplier retrieval ───────────────────────────────────────────────────

    [Fact]
    public async Task GetMultipliers_UnknownRule_DefaultsToOne()
    {
        var multipliers = await _sut.GetMultipliersAsync(["nonexistent-rule"]);

        multipliers.Should().BeEmpty(); // no stats row → not included, caller uses default 1.0
    }

    [Fact]
    public async Task GetMultipliers_KnownRule_ReturnsStoredMultiplier()
    {
        // Build enough samples to trigger adjustment
        for (var i = 0; i < 5; i++)
            await _sut.RecordTdkOutcomeAsync("rule-E", passed: true);

        await _sut.RecalculateAllAsync();

        var multipliers = await _sut.GetMultipliersAsync(["rule-E"]);

        multipliers.Should().ContainKey("rule-E");
        multipliers["rule-E"].Should().BeGreaterThan(1.0); // 100% pass → above neutral
    }

    // ── User-scoped overlay ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMultipliers_UserWithNegativeFeedback_LowerThanGlobal()
    {
        // Global signal: several passes (cross-user) → global multiplier above neutral.
        for (var i = 0; i < 4; i++)
            await _sut.RecordTdkOutcomeAsync("rule-U", passed: true);

        // user-X has its own (failing) experience with the same rule (>= 3 samples).
        for (var i = 0; i < 3; i++)
            await _sut.RecordTdkOutcomeAsync("rule-U", passed: false, userId: "user-X");

        await _sut.RecalculateAllAsync();

        var global     = await _sut.GetMultipliersAsync(["rule-U"]);
        var userScoped = await _sut.GetMultipliersAsync(["rule-U"], userId: "user-X");

        userScoped["rule-U"].Should().BeLessThan(global["rule-U"],
            because: "user-X's own negative feedback should degrade the rule for that user only");
    }

    [Fact]
    public async Task GetMultipliers_UserBelowSampleThreshold_FallsBackToGlobal()
    {
        for (var i = 0; i < 4; i++)
            await _sut.RecordTdkOutcomeAsync("rule-V", passed: true);

        // Only a single user event — below the 3-sample overlay threshold.
        await _sut.RecordTdkOutcomeAsync("rule-V", passed: false, userId: "user-Y");

        await _sut.RecalculateAllAsync();

        var global     = await _sut.GetMultipliersAsync(["rule-V"]);
        var userScoped = await _sut.GetMultipliersAsync(["rule-V"], userId: "user-Y");

        userScoped["rule-V"].Should().Be(global["rule-V"],
            because: "below the sample threshold the global multiplier is used unchanged");
    }

    [Fact]
    public async Task GetMultipliers_UserWithNoFeedback_EqualsGlobal()
    {
        for (var i = 0; i < 4; i++)
            await _sut.RecordTdkOutcomeAsync("rule-W", passed: true);
        await _sut.RecalculateAllAsync();

        var global     = await _sut.GetMultipliersAsync(["rule-W"]);
        var userScoped = await _sut.GetMultipliersAsync(["rule-W"], userId: "stranger");

        userScoped["rule-W"].Should().Be(global["rule-W"]);
    }

    // ── Recalculation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RecalculateAll_UpdatesMultipliersForAllRules()
    {
        await _sut.RecordTdkOutcomeAsync("rule-F", passed: true);
        await _sut.RecordTdkOutcomeAsync("rule-F", passed: true);
        await _sut.RecordTdkOutcomeAsync("rule-F", passed: true);

        // Before recalculation: default multiplier (1.0)
        var before = await _sut.GetStatsAsync("rule-F");
        before.ConfidenceMultiplier.Should().BeApproximately(1.0, 0.001);

        await _sut.RecalculateAllAsync();

        var after = await _sut.GetStatsAsync("rule-F");
        after.ConfidenceMultiplier.Should().BeGreaterThan(1.0);
        after.LastRecalculated.Should().NotBeNull();
    }

    // ── Confidence decay ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_AfterEvent_PopulatesLastFeedbackAt()
    {
        await _sut.RecordTdkOutcomeAsync("rule-ts", passed: true);

        var stats = await _sut.GetStatsAsync("rule-ts");

        stats.LastFeedbackAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RecalculateAll_StaleFeedback_DecaysMultiplierTowardNeutral()
    {
        // Insert 5 passing events dated 180 days ago (two half-lives at the default 90-day half-life).
        var old = TimeProvider.System.GetUtcNow().AddDays(-180);
        for (var i = 0; i < 5; i++)
        {
            await _store.AppendEventAsync(new RuleFeedbackEvent
            {
                EventId   = Guid.NewGuid().ToString(),
                RuleId    = "rule-stale",
                Type      = FeedbackEventType.TdkValidation,
                Positive  = true,
                Timestamp = old,
            }, default);
        }

        await _sut.RecalculateAllAsync();

        var stats = await _sut.GetStatsAsync("rule-stale");

        // Fresh this would be ~1.15; two half-lives old → deviation quartered → ~1.0375.
        stats.ConfidenceMultiplier.Should().BeGreaterThan(1.0);
        stats.ConfidenceMultiplier.Should().BeLessThan(1.10);
    }
}
