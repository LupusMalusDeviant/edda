using Edda.Agent.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tdk;

/// <summary>F16: <see cref="TdkEngine"/> integration of the optional LLM judge.</summary>
public class TdkEngineLlmJudgeTests
{
    private readonly Mock<ISandboxFactory> _sandboxFactory = new();
    private readonly Mock<IRuleConfidenceStore> _confidenceStore = new();
    private readonly Mock<ITdkLlmJudge> _judge = new();
    private readonly TdkHelperModule _helper = new();

    private TdkEngine Engine(bool withJudge = true, bool batchEnabled = false, ITdkResultCache? cache = null) => new(
        _sandboxFactory.Object,
        _confidenceStore.Object,
        NullLogger<TdkEngine>.Instance,
        _helper,
        resultCache: cache,
        batchEnabled: batchEnabled,
        llmJudge: withJudge ? _judge.Object : null);

    private static AgentRequest Request()
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("user-1");
        return new AgentRequest { UserMessage = "msg", ConversationId = "c1", Identity = identity.Object };
    }

    private static KnowledgeRule LlmRule(string id = "llm-rule", string? appliesTo = null) => new()
    {
        Id = id, Type = "Rule", Domain = "general", Priority = RulePriority.Medium, Body = "b",
        ValidatorType = "llm",
        ValidatorPrompt = "Error messages must be actionable.",
        AppliesTo = appliesTo is null ? [] : [appliesTo],
    };

    private void ArrangeVerdict(TdkJudgeResult result) =>
        _judge.Setup(j => j.JudgeAsync(It.IsAny<TdkJudgeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task LlmRule_JudgeFails_ReportsViolations_AndRecordsOutcome()
    {
        ArrangeVerdict(new TdkJudgeResult
        {
            Executed = true,
            Pass = false,
            Violations = [new TdkViolation("llm-rule", "vague error message", "warning")],
        });

        var result = await Engine().ValidateAsync(
            "```csharp\nthrow new Exception(\"error\");\n```", [LlmRule()], Request());

        result.HasViolations.Should().BeTrue();
        result.Violations.Should().ContainSingle().Which.Message.Should().Be("vague error message");
        _confidenceStore.Verify(s => s.RecordOutcome("llm-rule", false, It.IsAny<string?>()), Times.Once);
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never,
            "llm rules never run in the sandbox");
    }

    [Fact]
    public async Task LlmRule_JudgePasses_RecordsOutcome_NoViolations()
    {
        ArrangeVerdict(new TdkJudgeResult { Executed = true, Pass = true });

        var result = await Engine().ValidateAsync("```csharp\ncode\n```", [LlmRule()], Request());

        result.HasViolations.Should().BeFalse();
        result.EngineErrors.Should().BeEmpty();
        _confidenceStore.Verify(s => s.RecordOutcome("llm-rule", true, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task LlmRule_NoJudgeRegistered_SkippedSilently()
    {
        var result = await Engine(withJudge: false).ValidateAsync(
            "```csharp\ncode\n```", [LlmRule()], Request());

        result.HasViolations.Should().BeFalse();
        result.EngineErrors.Should().BeEmpty("off is off — no error, no outcome");
        _confidenceStore.Verify(
            s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LlmRule_JudgeNotExecuted_EngineError_NoConfidence()
    {
        ArrangeVerdict(new TdkJudgeResult { Executed = false, Error = "no provider configured" });

        var result = await Engine().ValidateAsync("```csharp\ncode\n```", [LlmRule()], Request());

        result.EngineErrors.Should().ContainSingle().Which.Reason.Should().Contain("no provider configured");
        _confidenceStore.Verify(
            s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task LlmRule_LanguageMismatch_SkippedBeforeJudge()
    {
        var result = await Engine().ValidateAsync(
            "```csharp\ncode\n```", [LlmRule(appliesTo: "python")], Request());

        result.HasViolations.Should().BeFalse();
        _judge.Verify(j => j.JudgeAsync(It.IsAny<TdkJudgeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LlmRule_RepeatedValidation_UsesCache()
    {
        ArrangeVerdict(new TdkJudgeResult
        {
            Executed = true,
            Pass = false,
            Violations = [new TdkViolation("llm-rule", "nope", "warning")],
        });
        var engine = Engine(cache: new InMemoryTdkResultCache());
        const string response = "```csharp\nbad\n```";

        var first = await engine.ValidateAsync(response, [LlmRule()], Request());
        var second = await engine.ValidateAsync(response, [LlmRule()], Request());

        first.HasViolations.Should().BeTrue();
        second.HasViolations.Should().BeTrue("the cached outcome still reports the violation");
        _judge.Verify(j => j.JudgeAsync(It.IsAny<TdkJudgeRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "the identical second validation reuses the cache instead of re-judging");
        _confidenceStore.Verify(s => s.RecordOutcome("llm-rule", false, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task BatchMode_LlmRule_JudgedOutsideTheBatchContainer()
    {
        ArrangeVerdict(new TdkJudgeResult
        {
            Executed = true,
            Pass = false,
            Violations = [new TdkViolation("llm-rule", "boom", "warning")],
        });

        var result = await Engine(batchEnabled: true).ValidateAsync(
            "```csharp\ncode\n```", [LlmRule()], Request());

        result.HasViolations.Should().BeTrue();
        _judge.Verify(j => j.JudgeAsync(It.IsAny<TdkJudgeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never,
            "an llm-only rule set creates no batch sandbox at all");
    }

    [Fact]
    public async Task LlmRule_PassesRequestFieldsToJudge()
    {
        TdkJudgeRequest? captured = null;
        _judge.Setup(j => j.JudgeAsync(It.IsAny<TdkJudgeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<TdkJudgeRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new TdkJudgeResult { Executed = true, Pass = true });

        await Engine().ValidateAsync("```csharp\nvar x = 1;\n```", [LlmRule()], Request());

        captured.Should().NotBeNull();
        captured!.RuleId.Should().Be("llm-rule");
        captured.Prompt.Should().Be("Error messages must be actionable.");
        captured.Language.Should().Be("csharp");
        captured.Code.Should().Contain("var x = 1;");
    }
}
