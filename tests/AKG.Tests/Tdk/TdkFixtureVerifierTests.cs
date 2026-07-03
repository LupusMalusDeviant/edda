using Edda.AKG.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Tdk;

/// <summary>F5: <see cref="TdkFixtureVerifier"/> — runs a rule's validator against its fixtures.</summary>
public class TdkFixtureVerifierTests
{
    private readonly Mock<ISandboxFactory> _sandboxFactory = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly Mock<ITdkHelperModule> _helper = new();

    public TdkFixtureVerifierTests()
    {
        _helper.SetupGet(h => h.FileName).Returns("tdk.py");
        _helper.SetupGet(h => h.Source).Returns("# helper source");
    }

    private TdkFixtureVerifier CreateSut(string knowledgeDirectory = "knowledge") => new(
        _fileSystem.Object,
        _sandboxFactory.Object,
        _helper.Object,
        NullLogger<TdkFixtureVerifier>.Instance,
        knowledgeDirectory);

    private static KnowledgeRule Rule(
        IReadOnlyList<string> pass, IReadOnlyList<string> fail, string? script = "validator.py") => new()
    {
        Id = "r1", Type = "Rule", Domain = "coding", Priority = RulePriority.Medium, Body = "b",
        ValidatorScript = script,
        ValidatorFixtures = new RuleValidatorFixtures { Pass = pass, Fail = fail },
    };

    private Mock<ISandbox> ArrangeSandbox(string stdout, int exitCode = 0, bool timedOut = false)
    {
        var sandbox = new Mock<ISandbox>();
        sandbox.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SandboxResult
            {
                ExitCode = exitCode, Stdout = stdout, Stderr = "stderr", TimedOut = timedOut
            });
        sandbox.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sandbox.Object);
        return sandbox;
    }

    [Fact]
    public async Task VerifyRuleAsync_PassFixtureNoViolations_CaseOk()
    {
        ArrangeSandbox("{\"pass\":true,\"violations\":[]}");
        var report = await CreateSut().VerifyRuleAsync(Rule(["ok=1"], []));

        report.HasFixtures.Should().BeTrue();
        report.Verified.Should().BeTrue();
        report.Cases.Should().ContainSingle().Which.Ok.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyRuleAsync_FailFixtureWithViolation_CaseOk()
    {
        ArrangeSandbox("{\"pass\":false,\"violations\":[{\"rule_id\":\"r1\",\"message\":\"x\",\"severity\":\"error\"}]}");
        var report = await CreateSut().VerifyRuleAsync(Rule([], ["bad=1"]));

        report.Verified.Should().BeTrue();
        report.Cases.Should().ContainSingle().Which.Ok.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyRuleAsync_FailFixtureNoViolation_NotVerified()
    {
        ArrangeSandbox("{\"pass\":true,\"violations\":[]}");
        var report = await CreateSut().VerifyRuleAsync(Rule([], ["bad=1"]));

        report.Verified.Should().BeFalse();
        report.Cases.Single().Ok.Should().BeFalse();
        report.Cases.Single().EngineError.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyRuleAsync_PassFixtureWithViolation_NotVerified()
    {
        ArrangeSandbox("{\"pass\":false,\"violations\":[{\"rule_id\":\"r1\",\"message\":\"x\",\"severity\":\"error\"}]}");
        var report = await CreateSut().VerifyRuleAsync(Rule(["ok=1"], []));

        report.Verified.Should().BeFalse();
        report.Cases.Single().Ok.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyRuleAsync_SandboxNonZeroExit_MarkedEngineError_NotVerified()
    {
        ArrangeSandbox(string.Empty, exitCode: 1);
        var report = await CreateSut().VerifyRuleAsync(Rule(["ok=1"], []));

        report.Verified.Should().BeFalse();
        report.Cases.Single().EngineError.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyRuleAsync_InvalidJson_MarkedEngineError()
    {
        ArrangeSandbox("NOT_JSON", exitCode: 0);
        var report = await CreateSut().VerifyRuleAsync(Rule(["ok=1"], []));

        report.Cases.Single().EngineError.Should().BeTrue();
        report.Verified.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyRuleAsync_RuleWithoutScript_HasFixturesFalse()
    {
        var report = await CreateSut().VerifyRuleAsync(Rule(["ok=1"], [], script: null));

        report.HasFixtures.Should().BeFalse();
        report.Cases.Should().BeEmpty();
        _sandboxFactory.Verify(f => f.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifyRuleAsync_DeliversHelperModuleToSandbox()
    {
        IReadOnlyDictionary<string, string>? captured = null;
        var sandbox = new Mock<ISandbox>();
        sandbox.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (_, _, files, _) => captured = files)
            .ReturnsAsync(new SandboxResult { ExitCode = 0, Stdout = "{\"pass\":true}", Stderr = "", TimedOut = false });
        sandbox.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _sandboxFactory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sandbox.Object);

        await CreateSut().VerifyRuleAsync(Rule(["ok=1"], []));

        captured.Should().NotBeNull();
        captured!.Should().ContainKey("tdk.py");
        captured!["tdk.py"].Should().Be("# helper source");
    }

    [Fact]
    public async Task VerifyAllAsync_DirectoryMissing_ReturnsEmpty()
    {
        _fileSystem.Setup(f => f.DirectoryExists("knowledge")).Returns(false);

        var report = await CreateSut().VerifyAllAsync();

        report.Rules.Should().BeEmpty();
        report.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task VerifyAllAsync_ParsesKnowledgeDir_OnlyRulesWithFixtures()
    {
        const string withFx =
            "---\nid: withfx\ntitle: With\ndomain: coding\npriority: High\nvalidatorScript: |\n  x=1\n" +
            "validatorFixtures:\n  pass:\n    - |\n      ok=1\n---\nBody.";
        const string withoutFx =
            "---\nid: nofx\ntitle: No\ndomain: coding\npriority: High\n---\nBody.";

        _fileSystem.Setup(f => f.DirectoryExists("knowledge")).Returns(true);
        _fileSystem.Setup(f => f.EnumerateFiles("knowledge", "*.md", true)).Returns(["a.md", "b.md"]);
        _fileSystem.Setup(f => f.ReadAllTextAsync("a.md", It.IsAny<CancellationToken>())).ReturnsAsync(withFx);
        _fileSystem.Setup(f => f.ReadAllTextAsync("b.md", It.IsAny<CancellationToken>())).ReturnsAsync(withoutFx);
        ArrangeSandbox("{\"pass\":true}");

        var report = await CreateSut().VerifyAllAsync();

        report.Rules.Should().ContainSingle().Which.RuleId.Should().Be("withfx");
        report.VerifiedCount.Should().Be(1);
    }
}
