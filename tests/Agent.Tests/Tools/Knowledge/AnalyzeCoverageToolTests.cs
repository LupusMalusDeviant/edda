using System.Text.Json;
using Edda.Agent.Tools.Knowledge;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Knowledge;

public class AnalyzeCoverageToolTests
{
    private readonly Mock<IKnowledgeGraph> _kg = new();
    private readonly Mock<IRuleFeedbackService> _feedback = new();
    private readonly AnalyzeCoverageTool _sut;

    public AnalyzeCoverageToolTests()
    {
        // Default: no feedback rows. Individual tests override where needed.
        _feedback.Setup(f => f.GetAllStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _sut = new AnalyzeCoverageTool(
            _kg.Object, _feedback.Object, TimeProvider.System,
            NullLogger<AnalyzeCoverageTool>.Instance);
    }

    private static ToolCall Call(int? staleDays = null)
    {
        var args = new Dictionary<string, object?>();
        if (staleDays is not null) args["stale_days"] = staleDays.Value;
        return new ToolCall { Id = "tc-1", Name = "analyze_coverage", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    private static KnowledgeRule Rule(string id, string domain = "csharp", RuleRelations? rel = null) =>
        new()
        {
            Id        = id,
            Type      = "convention",
            Domain    = domain,
            Priority  = RulePriority.Medium,
            Body      = $"body {id}",
            Tags      = [],
            RelatesTo = rel
        };

    private void SetupRules(params KnowledgeRule[] rules) =>
        _kg.Setup(k => k.GetRulesAsync(null, null, null, "user-1", It.IsAny<CancellationToken>()))
           .ReturnsAsync(rules);

    private static JsonElement Section(ToolResult result, string name)
    {
        using var doc = JsonDocument.Parse(result.Content!);
        return doc.RootElement.GetProperty(name).Clone();
    }

    [Fact]
    public void Definition_HasCorrectName()
        => _sut.Definition.Name.Should().Be("analyze_coverage");

    [Fact]
    public async Task ExecuteAsync_ThinDomain_IsFlagged()
    {
        SetupRules(Rule("rule-1", domain: "lonely"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        var thin = Section(result, "thinDomains");
        thin.GetProperty("count").GetInt32().Should().Be(1);
        thin.GetProperty("items")[0].GetProperty("domain").GetString().Should().Be("lonely");
    }

    [Fact]
    public async Task ExecuteAsync_DanglingReference_IsFlagged()
    {
        SetupRules(Rule("rule-a", rel: new RuleRelations { Requires = ["ghost-rule"] }));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        var dangling = Section(result, "danglingReferences");
        dangling.GetProperty("count").GetInt32().Should().Be(1);
        dangling.GetProperty("items")[0].GetProperty("missingTarget").GetString().Should().Be("ghost-rule");
    }

    [Fact]
    public async Task ExecuteAsync_UnresolvedConflict_IsFlaggedOncePerPair()
    {
        // Both rules exist and both declare the conflict → reported once (de-duplicated pair).
        SetupRules(
            Rule("rule-a", rel: new RuleRelations { ConflictsWith = ["rule-b"] }),
            Rule("rule-b", rel: new RuleRelations { ConflictsWith = ["rule-a"] }));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        Section(result, "conflicts").GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_StaleRule_IsFlagged()
    {
        SetupRules(Rule("rule-stale"));
        _feedback.Setup(f => f.GetAllStatsAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new RuleFeedbackStats
                 {
                     RuleId         = "rule-stale",
                     LastFeedbackAt = TimeProvider.System.GetUtcNow().AddDays(-200)
                 }]);

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        var stale = Section(result, "stale");
        stale.GetProperty("count").GetInt32().Should().Be(1);
        stale.GetProperty("items")[0].GetProperty("ruleId").GetString().Should().Be("rule-stale");
    }

    [Fact]
    public async Task ExecuteAsync_LowConfidenceRule_IsFlagged()
    {
        SetupRules(Rule("rule-weak"));
        _feedback.Setup(f => f.GetAllStatsAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new RuleFeedbackStats { RuleId = "rule-weak", ConfidenceMultiplier = 0.5 }]);

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        var low = Section(result, "lowConfidence");
        low.GetProperty("count").GetInt32().Should().Be(1);
        low.GetProperty("items")[0].GetProperty("ruleId").GetString().Should().Be("rule-weak");
    }

    [Fact]
    public async Task ExecuteAsync_CustomStaleDays_IsHonored()
    {
        SetupRules(Rule("rule-x"));
        _feedback.Setup(f => f.GetAllStatsAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new RuleFeedbackStats
                 {
                     RuleId         = "rule-x",
                     LastFeedbackAt = TimeProvider.System.GetUtcNow().AddDays(-30)
                 }]);

        // 30-day-old feedback is NOT stale at the default 90 days...
        var def = await _sut.ExecuteAsync(Call(), Ctx());
        Section(def, "stale").GetProperty("count").GetInt32().Should().Be(0);

        // ...but IS stale when the caller lowers the window to 10 days.
        var custom = await _sut.ExecuteAsync(Call(staleDays: 10), Ctx());
        Section(custom, "stale").GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_KgThrows_ReturnsFail()
    {
        _kg.Setup(k => k.GetRulesAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                  It.IsAny<string?>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("graph error"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("graph error");
    }
}
