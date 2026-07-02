using Edda.Agent.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tdk;

public class TdkEngineTests
{
    private readonly Mock<ISandboxFactory> _sandboxFactory = new();
    private readonly Mock<IRuleConfidenceStore> _confidenceStore = new();
    private readonly TdkEngine _sut;

    public TdkEngineTests()
    {
        _sut = new TdkEngine(
            _sandboxFactory.Object,
            _confidenceStore.Object,
            NullLogger<TdkEngine>.Instance);
    }

    private static AgentRequest CreateDefaultRequest()
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("user-1");
        identity.SetupGet(i => i.Username).Returns("alice");
        identity.SetupGet(i => i.TenantId).Returns("default");
        identity.SetupGet(i => i.IsClone).Returns(false);
        identity.SetupGet(i => i.IsAdmin).Returns(false);
        return new AgentRequest
        {
            UserMessage = "Write me some code",
            ConversationId = "conv-1",
            Identity = identity.Object
        };
    }

    [Fact]
    public async Task TdkEngine_NoValidatorRules_SkipsValidation()
    {
        var rules = new List<KnowledgeRule>
        {
            new() { Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium, Body = "Never do X." }
            // No ValidatorScript set
        };

        var result = await _sut.ValidateAsync(
            "Some response with ```python\ncode\n```", rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TdkEngine_NoCodeBlocks_SkipsValidation()
    {
        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Never do X.", ValidatorScript = "print('hello')"
            }
        };

        var result = await _sut.ValidateAsync(
            "No code blocks here, just plain text.", rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TdkEngine_ValidatorPass_ReturnsNoViolations()
    {
        var sandbox = SetupMockSandbox("{\"pass\":true,\"violations\":[]}", exitCode: 0);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Some rule.", ValidatorScript = "validator.py content"
            }
        };

        var result = await _sut.ValidateAsync(
            "Here is code:\n```python\nprint('hello')\n```",
            rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task TdkEngine_ValidatorFail_ReturnsViolations()
    {
        const string validatorOutput =
            "{\"pass\":false,\"violations\":[{\"rule_id\":\"r1\",\"message\":\"Plaintext secret detected\",\"severity\":\"error\"}]}";
        var sandbox = SetupMockSandbox(validatorOutput, exitCode: 0);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "security", Priority = RulePriority.Medium,
                Body = "Never store plaintext secrets.", ValidatorScript = "validator.py content"
            }
        };

        var result = await _sut.ValidateAsync(
            "Here is code:\n```python\npassword = 'secret'\n```",
            rules, CreateDefaultRequest());

        result.HasViolations.Should().BeTrue();
        result.Violations.Should().HaveCount(1);
        result.Violations[0].RuleId.Should().Be("r1");
        result.Violations[0].Message.Should().Be("Plaintext secret detected");
        result.Violations[0].Severity.Should().Be("error");
    }

    [Fact]
    public async Task TdkEngine_ValidatorCrash_LogsAndContinues()
    {
        var sandbox = SetupMockSandbox(string.Empty, exitCode: 1, timedOut: false);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Some rule.", ValidatorScript = "crashing_validator.py"
            }
        };

        // Should NOT throw — logs warning and continues
        var result = await _sut.ValidateAsync(
            "```python\ncode\n```",
            rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
    }

    [Fact]
    public async Task TdkEngine_ValidatorTimeout_SkipsRule()
    {
        var sandbox = SetupMockSandbox(string.Empty, exitCode: 1, timedOut: true);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Slow rule.", ValidatorScript = "slow_validator.py"
            }
        };

        var result = await _sut.ValidateAsync(
            "```python\ncode\n```",
            rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
    }

    [Fact]
    public async Task TdkEngine_RecordsOutcome_InConfidenceStore_OnPass()
    {
        var sandbox = SetupMockSandbox("{\"pass\":true,\"violations\":[]}", exitCode: 0);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Some rule.", ValidatorScript = "validator.py"
            }
        };

        await _sut.ValidateAsync("```python\ncode\n```", rules, CreateDefaultRequest());

        _confidenceStore.Verify(s => s.RecordOutcome("r1", true), Times.Once);
    }

    [Fact]
    public async Task TdkEngine_RecordsOutcome_InConfidenceStore_OnViolation()
    {
        const string failOutput =
            "{\"pass\":false,\"violations\":[{\"rule_id\":\"r1\",\"message\":\"msg\",\"severity\":\"error\"}]}";
        var sandbox = SetupMockSandbox(failOutput, exitCode: 0);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Never do X.", ValidatorScript = "validator.py"
            }
        };

        await _sut.ValidateAsync("```python\nbad_code\n```", rules, CreateDefaultRequest());

        _confidenceStore.Verify(s => s.RecordOutcome("r1", false), Times.Once);
    }

    [Fact]
    public async Task TdkEngine_ValidatorCrash_DoesNotRecordOutcome_ButReportsEngineError()
    {
        var sandbox = SetupMockSandbox(string.Empty, exitCode: 127);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Some rule.", ValidatorScript = "validator.py"
            }
        };

        var result = await _sut.ValidateAsync("```python\ncode\n```", rules, CreateDefaultRequest());

        // F3: an infrastructure failure must NOT be booked as a business pass/fail outcome.
        _confidenceStore.Verify(s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        result.HasViolations.Should().BeFalse();
        result.EngineErrors.Should().ContainSingle(e => e.RuleId == "r1");
        result.EngineErrors[0].ExitCode.Should().Be(127);
    }

    [Fact]
    public async Task TdkEngine_InvalidJson_DoesNotRecordOutcome_ButReportsEngineError()
    {
        var sandbox = SetupMockSandbox("NOT_VALID_JSON", exitCode: 0);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Some rule.", ValidatorScript = "validator.py"
            }
        };

        var result = await _sut.ValidateAsync("```python\ncode\n```", rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
        _confidenceStore.Verify(s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        result.EngineErrors.Should().ContainSingle(e => e.RuleId == "r1");
    }

    [Fact]
    public async Task TdkEngine_ValidatorTimeout_ReportsTimedOutEngineError_WithoutRecordingOutcome()
    {
        var sandbox = SetupMockSandbox(string.Empty, exitCode: 1, timedOut: true);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "r1", Type = "rule", Domain = "general", Priority = RulePriority.Medium,
                Body = "Slow rule.", ValidatorScript = "slow_validator.py"
            }
        };

        var result = await _sut.ValidateAsync("```python\ncode\n```", rules, CreateDefaultRequest());

        result.EngineErrors.Should().ContainSingle(e => e.RuleId == "r1" && e.TimedOut);
        _confidenceStore.Verify(s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task TdkEngine_RuleDoesNotTargetBlockLanguage_SkipsBeforeSandbox()
    {
        // F9: the rule targets python but the response only has a csharp block, so the (rule × block)
        // pair is skipped before any sandbox is created — no validator runs, no outcome/engine error.
        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "py-only", Type = "rule", Domain = "python", Priority = RulePriority.Medium,
                Body = "Python-only rule.", ValidatorScript = "validator.py", AppliesTo = ["python"]
            }
        };

        var result = await _sut.ValidateAsync(
            "```csharp\nvar x = 1;\n```", rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
        result.EngineErrors.Should().BeEmpty();
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never,
            "a rule that does not target the block language must be skipped before a sandbox is created");
        _confidenceStore.Verify(s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task TdkEngine_RuleTargetsBlockLanguage_RunsValidator()
    {
        var sandbox = SetupMockSandbox("{\"pass\":true,\"violations\":[]}", exitCode: 0);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sandbox.Object);

        var rules = new List<KnowledgeRule>
        {
            new()
            {
                Id = "cs-rule", Type = "rule", Domain = "csharp", Priority = RulePriority.Medium,
                Body = "C#-only rule.", ValidatorScript = "validator.py", AppliesTo = ["csharp"]
            }
        };

        var result = await _sut.ValidateAsync(
            "```csharp\nvar x = 1;\n```", rules, CreateDefaultRequest());

        result.HasViolations.Should().BeFalse();
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Once,
            "a rule targeting the block language runs its validator");
    }

    private static Mock<ISandbox> SetupMockSandbox(
        string stdout, int exitCode, bool timedOut = false)
    {
        var sandbox = new Mock<ISandbox>();
        sandbox.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SandboxResult
            {
                ExitCode = exitCode,
                Stdout = stdout,
                Stderr = string.Empty,
                TimedOut = timedOut
            });
        sandbox.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return sandbox;
    }
}
