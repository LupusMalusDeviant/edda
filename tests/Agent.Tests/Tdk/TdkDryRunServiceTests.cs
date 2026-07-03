using Edda.Agent.Tdk;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Agent.Tests.Tdk;

/// <summary>F6: <see cref="TdkDryRunService"/> — runs an arbitrary script against sample code.</summary>
public class TdkDryRunServiceTests
{
    private readonly Mock<ISandboxFactory> _factory = new();
    private readonly Mock<ITdkHelperModule> _helper = new();

    public TdkDryRunServiceTests()
    {
        _helper.SetupGet(h => h.FileName).Returns("tdk.py");
        _helper.SetupGet(h => h.Source).Returns("# helper source");
    }

    private TdkDryRunService CreateSut() =>
        new(_factory.Object, _helper.Object, NullLogger<TdkDryRunService>.Instance);

    private Mock<ISandbox> ArrangeSandbox(string stdout, int exitCode = 0, bool timedOut = false, string stderr = "")
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
    public async Task RunAsync_ValidatorReportsViolations_ParsesThem()
    {
        ArrangeSandbox(
            "{\"pass\":false,\"violations\":[{\"rule_id\":\"dry-run\",\"message\":\"boom\",\"severity\":\"error\",\"line\":3,\"suggestion\":\"fix it\"}]}");

        var result = await CreateSut().RunAsync("script", "code", "python");

        result.OutputParsed.Should().BeTrue();
        result.Pass.Should().BeFalse();
        result.Violations.Should().ContainSingle();
        result.Violations[0].RuleId.Should().Be("dry-run");
        result.Violations[0].Message.Should().Be("boom");
        result.Violations[0].Line.Should().Be(3);
        result.Violations[0].Suggestion.Should().Be("fix it");
    }

    [Fact]
    public async Task RunAsync_ValidatorPasses_NoViolations()
    {
        ArrangeSandbox("{\"pass\":true,\"violations\":[]}");

        var result = await CreateSut().RunAsync("script", "code");

        result.OutputParsed.Should().BeTrue();
        result.Pass.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_InvalidJsonStdout_OutputParsedFalse_KeepsRawStdout()
    {
        ArrangeSandbox("not json at all", exitCode: 0);

        var result = await CreateSut().RunAsync("script", "code");

        result.OutputParsed.Should().BeFalse();
        result.Stdout.Should().Be("not json at all");
    }

    [Fact]
    public async Task RunAsync_SandboxNonZeroExit_ReturnsExitAndStderr_OutputParsedFalse()
    {
        ArrangeSandbox(string.Empty, exitCode: 1, stderr: "Traceback...");

        var result = await CreateSut().RunAsync("script", "code");

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Be("Traceback...");
        result.OutputParsed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_SandboxThrows_ReturnsExitMinusOneWithMessage()
    {
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no docker"));

        var result = await CreateSut().RunAsync("script", "code");

        result.ExitCode.Should().Be(-1);
        result.Stderr.Should().Contain("no docker");
        result.OutputParsed.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_DeliversHelperModuleToSandbox()
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
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sandbox.Object);

        await CreateSut().RunAsync("script", "code");

        captured.Should().NotBeNull();
        captured!.Should().ContainKey("tdk.py");
        captured!["tdk.py"].Should().Be("# helper source");
    }

    [Fact]
    public async Task RunAsync_NullLanguage_SendsEmptyLanguage()
    {
        string? capturedJson = null;
        var sandbox = new Mock<ISandbox>();
        sandbox.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (_, json, _, _) => capturedJson = json)
            .ReturnsAsync(new SandboxResult { ExitCode = 0, Stdout = "{\"pass\":true}", Stderr = "", TimedOut = false });
        sandbox.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _factory.Setup(f => f.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(sandbox.Object);

        await CreateSut().RunAsync("script", "code", language: null);

        capturedJson.Should().Contain("\"language\":\"\"");
    }
}
