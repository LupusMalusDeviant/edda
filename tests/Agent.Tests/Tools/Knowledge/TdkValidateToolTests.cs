using Edda.Agent.Tools.Knowledge;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tools.Knowledge;

public class TdkValidateToolTests
{
    private readonly Mock<ITdkEngine> _tdk = new();
    private readonly Mock<IKnowledgeGraph> _kg = new();
    private readonly TdkValidateTool _sut;

    public TdkValidateToolTests()
    {
        _sut = new TdkValidateTool(_tdk.Object, _kg.Object, NullLogger<TdkValidateTool>.Instance);
    }

    private static ToolCall Call(string? code = "print('x')", string? query = null)
    {
        var args = new Dictionary<string, object?>();
        if (code is not null) args["code"] = code;
        if (query is not null) args["query"] = query;
        return new ToolCall { Id = "tc-1", Name = "tdk_validate", Arguments = args };
    }

    private static ToolExecutionContext Ctx(string userId = "user-1") =>
        new() { ConversationId = "conv-1", UserId = userId };

    private void SetupContext(params KnowledgeRule[] rules) =>
        _kg.Setup(k => k.CompileContextAsync(It.IsAny<TaskContext>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new ContextResult { FormattedContext = "", ActiveRules = rules });

    private void SetupValidation(TdkResult result) =>
        _tdk.Setup(t => t.ValidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<KnowledgeRule>>(),
                It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static KnowledgeRule Rule(string id) => new()
    {
        Id = id,
        Type = "constraint",
        Domain = "security",
        Priority = RulePriority.High,
        Body = $"Body for {id}",
        Tags = []
    };

    [Fact]
    public async Task ExecuteAsync_NoViolations_ReturnsCleanMessage()
    {
        SetupContext(Rule("r1"));
        SetupValidation(TdkResult.NoViolations);

        // Explicit query exercises the query-argument branch.
        var result = await _sut.ExecuteAsync(Call(query: "security review"), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("No knowledge-base violations");
    }

    [Fact]
    public async Task ExecuteAsync_WithViolations_ReturnsFormattedReport()
    {
        SetupContext(Rule("never-store-plaintext-secrets"));
        SetupValidation(new TdkResult
        {
            HasViolations = true,
            Violations =
            [
                new TdkViolation("never-store-plaintext-secrets", "Plaintext secret detected", "critical")
            ]
        });

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeTrue();
        result.Content.Should().Contain("never-store-plaintext-secrets");
        result.Content.Should().Contain("Plaintext secret detected");
    }

    [Fact]
    public async Task ExecuteAsync_MissingCode_ReturnsFail()
    {
        var result = await _sut.ExecuteAsync(Call(code: null), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("code");
    }

    [Fact]
    public async Task ExecuteAsync_PassesCompiledRulesAndIdentityToEngine()
    {
        SetupContext(Rule("r1"), Rule("r2"));
        IReadOnlyList<KnowledgeRule>? capturedRules = null;
        AgentRequest? capturedRequest = null;
        _tdk.Setup(t => t.ValidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<KnowledgeRule>>(),
                It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<KnowledgeRule>, AgentRequest, CancellationToken>(
                (_, rules, req, _) => { capturedRules = rules; capturedRequest = req; })
            .ReturnsAsync(TdkResult.NoViolations);

        await _sut.ExecuteAsync(Call(), Ctx("user-7"));

        capturedRules!.Select(r => r.Id).Should().Equal("r1", "r2");
        capturedRequest!.Identity.UserId.Should().Be("user-7");
        capturedRequest.Identity.Username.Should().BeNull();
        capturedRequest.Identity.TenantId.Should().Be("system");
        capturedRequest.Identity.IsClone.Should().BeFalse();
        capturedRequest.Identity.IsAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_EngineThrows_ReturnsFail()
    {
        SetupContext(Rule("r1"));
        _tdk.Setup(t => t.ValidateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<KnowledgeRule>>(),
                It.IsAny<AgentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sandbox boom"));

        var result = await _sut.ExecuteAsync(Call(), Ctx());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("sandbox boom");
    }

    [Fact]
    public void Definition_HasCorrectName()
    {
        _sut.Definition.Name.Should().Be("tdk_validate");
    }
}
