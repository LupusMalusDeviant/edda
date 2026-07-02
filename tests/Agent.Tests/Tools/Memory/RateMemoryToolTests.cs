using Edda.Agent.Tools.Memory;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Memory;

public class RateMemoryToolTests
{
    private readonly Mock<IRuleFeedbackService> _feedback = new();
    private readonly RateMemoryTool _sut;

    public RateMemoryToolTests()
    {
        _feedback.Setup(f => f.RecordRuleRatingAsync(
                  It.IsAny<string>(), It.IsAny<RuleRating>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        _sut = new RateMemoryTool(_feedback.Object, NullLogger<RateMemoryTool>.Instance);
    }

    private static ToolCall Call(string? ruleId = "rule-1", string? outcome = "helpful")
    {
        var args = new Dictionary<string, object?>();
        if (ruleId is not null) args["ruleId"] = ruleId;
        if (outcome is not null) args["outcome"] = outcome;
        return new ToolCall { Id = "tc-1", Name = "rate_memory", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string? userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    [Fact]
    public async Task ExecuteAsync_Helpful_RecordsHelpfulRating_ScopedToContextUser()
    {
        var result = await _sut.ExecuteAsync(Call("rule-1", "helpful"), Ctx("alice"));

        result.Success.Should().BeTrue();
        _feedback.Verify(f => f.RecordRuleRatingAsync(
            "rule-1", RuleRating.Helpful, "alice", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NotHelpful_RecordsNotHelpfulRating()
    {
        await _sut.ExecuteAsync(Call("rule-1", "not_helpful"), Ctx());

        _feedback.Verify(f => f.RecordRuleRatingAsync(
            "rule-1", RuleRating.NotHelpful, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Outdated_RecordsOutdatedRating()
    {
        await _sut.ExecuteAsync(Call("rule-1", "outdated"), Ctx());

        _feedback.Verify(f => f.RecordRuleRatingAsync(
            "rule-1", RuleRating.Outdated, "user-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRuleId_ReturnsFail_WithoutRecording()
    {
        var result = await _sut.ExecuteAsync(Call(ruleId: null), Ctx());

        result.Success.Should().BeFalse();
        _feedback.Verify(f => f.RecordRuleRatingAsync(
            It.IsAny<string>(), It.IsAny<RuleRating>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_BlankRuleId_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call(ruleId: "   "), Ctx());

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidOutcome_ReturnsFail_WithoutRecording()
    {
        var result = await _sut.ExecuteAsync(Call("rule-1", "lol"), Ctx());

        result.Success.Should().BeFalse();
        _feedback.Verify(f => f.RecordRuleRatingAsync(
            It.IsAny<string>(), It.IsAny<RuleRating>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NullUserId_UsesAnonymous()
    {
        await _sut.ExecuteAsync(Call(), Ctx(userId: null));

        _feedback.Verify(f => f.RecordRuleRatingAsync(
            "rule-1", RuleRating.Helpful, "anonymous", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrows_ReturnsFail()
    {
        _feedback.Setup(f => f.RecordRuleRatingAsync(
                  It.IsAny<string>(), It.IsAny<RuleRating>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("feedback store down"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("feedback store down");
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("rate_memory");
    }
}
