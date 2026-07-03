using System.Text.Json;
using Edda.Agent.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tdk;

/// <summary>F11: <see cref="TdkEngine"/> batch path (one sandbox for all jobs).</summary>
public class TdkEngineBatchTests
{
    private readonly Mock<ISandboxFactory> _factory = new();
    private readonly Mock<IRuleConfidenceStore> _store = new();
    private readonly TdkHelperModule _helper = new();

    private TdkEngine BatchEngine(ITdkResultCache? cache = null) => new(
        _factory.Object, _store.Object, NullLogger<TdkEngine>.Instance, _helper,
        resultCache: cache, batchEnabled: true);

    private static AgentRequest Request()
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("user-1");
        identity.SetupGet(i => i.TenantId).Returns("default");
        return new AgentRequest { UserMessage = "msg", ConversationId = "c1", Identity = identity.Object };
    }

    private static KnowledgeRule Rule(string id, string? appliesTo = null) => new()
    {
        Id = id, Type = "Rule", Domain = "general", Priority = RulePriority.Medium, Body = "b",
        ValidatorScript = "validator.py",
        AppliesTo = appliesTo is null ? [] : [appliesTo],
    };

    private const string PassStdout = "{\"pass\":true,\"violations\":[]}";

    private static string FailStdout(string ruleId) =>
        "{\"pass\":false,\"violations\":[{\"rule_id\":\"" + ruleId + "\",\"message\":\"boom\",\"severity\":\"error\"}]}";

    private static object Job(int id, string stdout, int exit = 0, bool timedOut = false, string stderr = "")
        => new { id, exit_code = exit, stdout, stderr, timed_out = timedOut };

    private static string BatchJson(params object[] results) => JsonSerializer.Serialize(new { results });

    private Mock<ISandbox> ArrangeBatchSandbox(string stdout, int exitCode = 0, bool timedOut = false, string stderr = "")
    {
        var sandbox = new Mock<ISandbox>();
        sandbox.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SandboxResult
            {
                ExitCode = exitCode, Stdout = stdout, Stderr = stderr, TimedOut = timedOut
            });
        sandbox.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sandbox.Object);
        return sandbox;
    }

    [Fact]
    public async Task Batch_MultipleRules_RunsSingleSandbox()
    {
        ArrangeBatchSandbox(BatchJson(Job(0, PassStdout), Job(1, PassStdout)));
        var rules = new List<KnowledgeRule> { Rule("r1"), Rule("r2") };

        await BatchEngine().ValidateAsync("```python\ncode\n```", rules, Request());

        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Once,
            "batch mode runs every (rule × block) pair in a single sandbox");
    }

    [Fact]
    public async Task Batch_ValidatorFails_MapsViolationToRule()
    {
        ArrangeBatchSandbox(BatchJson(Job(0, FailStdout("r1"))));

        var result = await BatchEngine().ValidateAsync(
            "```python\ncode\n```", new List<KnowledgeRule> { Rule("r1") }, Request());

        result.HasViolations.Should().BeTrue();
        result.Violations.Should().ContainSingle().Which.RuleId.Should().Be("r1");
    }

    [Fact]
    public async Task Batch_ValidatorPasses_RecordsOutcome_NoViolation()
    {
        ArrangeBatchSandbox(BatchJson(Job(0, PassStdout)));

        var result = await BatchEngine().ValidateAsync(
            "```python\ncode\n```", new List<KnowledgeRule> { Rule("r1") }, Request());

        result.HasViolations.Should().BeFalse();
        _store.Verify(s => s.RecordOutcome("r1", true, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Batch_JobNonZeroExit_ReportsEngineError_NoOutcome()
    {
        ArrangeBatchSandbox(BatchJson(Job(0, "", exit: 127, stderr: "boom")));

        var result = await BatchEngine().ValidateAsync(
            "```python\ncode\n```", new List<KnowledgeRule> { Rule("r1") }, Request());

        result.EngineErrors.Should().ContainSingle(e => e.RuleId == "r1");
        result.EngineErrors[0].ExitCode.Should().Be(127);
        _store.Verify(s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Batch_RunnerInvalidJson_AllJobsEngineError()
    {
        ArrangeBatchSandbox("NOT_JSON");

        var result = await BatchEngine().ValidateAsync(
            "```python\ncode\n```", new List<KnowledgeRule> { Rule("r1") }, Request());

        result.EngineErrors.Should().ContainSingle(e => e.RuleId == "r1");
        _store.Verify(s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Batch_RunnerNonZeroExit_AllJobsEngineError()
    {
        ArrangeBatchSandbox(string.Empty, exitCode: 1);
        var rules = new List<KnowledgeRule> { Rule("r1"), Rule("r2") };

        var result = await BatchEngine().ValidateAsync("```python\ncode\n```", rules, Request());

        result.EngineErrors.Should().HaveCount(2);
        _store.Verify(s => s.RecordOutcome(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Batch_DeliversRunnerAndHelper_ToSandbox()
    {
        string? capturedScript = null;
        IReadOnlyDictionary<string, string>? capturedFiles = null;
        var sandbox = new Mock<ISandbox>();
        sandbox.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (script, _, files, _) => { capturedScript = script; capturedFiles = files; })
            .ReturnsAsync(new SandboxResult
            {
                ExitCode = 0, Stdout = BatchJson(Job(0, PassStdout)), Stderr = "", TimedOut = false
            });
        sandbox.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sandbox.Object);

        await BatchEngine().ValidateAsync(
            "```python\ncode\n```", new List<KnowledgeRule> { Rule("r1") }, Request());

        capturedScript.Should().Be(TdkBatchRunner.Source);
        capturedFiles.Should().NotBeNull();
        capturedFiles!.Should().ContainKey("tdk.py");
    }

    [Fact]
    public async Task Batch_LanguageMismatch_ExcludedFromJobs()
    {
        // F9 applies in batch too: a python-only rule against a csharp block produces no job → no sandbox.
        var rules = new List<KnowledgeRule> { Rule("py-only", appliesTo: "python") };

        var result = await BatchEngine().ValidateAsync("```csharp\nvar x=1;\n```", rules, Request());

        result.HasViolations.Should().BeFalse();
        result.EngineErrors.Should().BeEmpty();
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Batch_CacheHit_SkipsJob_StillReportsViolation()
    {
        // F13 applies in batch too: the second identical validation reuses the cached outcome and
        // creates no second sandbox, but still reports the violation.
        ArrangeBatchSandbox(BatchJson(Job(0, FailStdout("r1"))));
        var engine = BatchEngine(cache: new InMemoryTdkResultCache());
        var rules = new List<KnowledgeRule> { Rule("r1") };
        const string response = "```python\nbad\n```";

        var first = await engine.ValidateAsync(response, rules, Request());
        var second = await engine.ValidateAsync(response, rules, Request());

        first.HasViolations.Should().BeTrue();
        second.HasViolations.Should().BeTrue("the cached outcome still reports the violation");
        _factory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Once,
            "the identical second validation reuses the cache instead of re-running the sandbox");
    }
}
