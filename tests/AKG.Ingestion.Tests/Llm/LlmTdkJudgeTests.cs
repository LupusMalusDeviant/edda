using Edda.AKG.Ingestion.Llm;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Llm;

/// <summary>F16: <see cref="LlmTdkJudge"/> — verdict parsing, hardening and failure modes.</summary>
public sealed class LlmTdkJudgeTests
{
    private readonly Mock<ILlmChatClient> _chatClient = new();

    private LlmTdkJudge CreateSut() => new(_chatClient.Object, NullLogger<LlmTdkJudge>.Instance);

    private static TdkJudgeRequest Request(string code = "var x = 1;") => new()
    {
        RuleId = "actionable-errors",
        Prompt = "Error messages must be actionable.",
        Code = code,
        Language = "csharp",
    };

    private void ArrangeAnswer(string answer) =>
        _chatClient.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer);

    [Fact]
    public async Task JudgeAsync_ValidJson_MapsPassAndViolationsWithRuleId()
    {
        ArrangeAnswer(
            "{\"pass\":false,\"violations\":[{\"message\":\"vague error\",\"severity\":\"error\",\"line\":3,\"suggestion\":\"name the cause\"}]}");

        var result = await CreateSut().JudgeAsync(Request());

        result.Executed.Should().BeTrue();
        result.Pass.Should().BeFalse();
        result.Violations.Should().ContainSingle();
        result.Violations[0].RuleId.Should().Be("actionable-errors");
        result.Violations[0].Message.Should().Be("vague error");
        result.Violations[0].Line.Should().Be(3);
        result.Violations[0].Suggestion.Should().Be("name the cause");
    }

    [Fact]
    public async Task JudgeAsync_JsonInMarkdownFence_Parsed()
    {
        ArrangeAnswer("```json\n{\"pass\":true,\"violations\":[]}\n```");

        var result = await CreateSut().JudgeAsync(Request());

        result.Executed.Should().BeTrue();
        result.Pass.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task JudgeAsync_GarbageAnswer_ExecutedFalse()
    {
        ArrangeAnswer("Sure! The code looks fine to me.");

        var result = await CreateSut().JudgeAsync(Request());

        result.Executed.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task JudgeAsync_EmptyAnswer_ExecutedFalse()
    {
        ArrangeAnswer("");

        var result = await CreateSut().JudgeAsync(Request());

        result.Executed.Should().BeFalse();
    }

    [Fact]
    public async Task JudgeAsync_ClientThrows_ExecutedFalseWithError()
    {
        _chatClient.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no provider configured"));

        var result = await CreateSut().JudgeAsync(Request());

        result.Executed.Should().BeFalse();
        result.Error.Should().Contain("no provider configured");
    }

    [Fact]
    public async Task JudgeAsync_MissingSeverity_DefaultsToWarning()
    {
        ArrangeAnswer("{\"pass\":false,\"violations\":[{\"message\":\"meh\"}]}");

        var result = await CreateSut().JudgeAsync(Request());

        result.Violations.Should().ContainSingle().Which.Severity.Should().Be("warning");
    }

    [Fact]
    public async Task JudgeAsync_SystemPrompt_ContainsInjectionGuardAndJsonOnly()
    {
        string? capturedSystem = null;
        _chatClient.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((sys, _, _) => capturedSystem = sys)
            .ReturnsAsync("{\"pass\":true}");

        await CreateSut().JudgeAsync(Request());

        capturedSystem.Should().NotBeNull();
        capturedSystem!.Should().Contain("DATA", because: "the code must be declared data, not instructions");
        capturedSystem.Should().Contain("ONLY a JSON object");
    }
}
