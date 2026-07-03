using Edda.Sandboxing.Wasm;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Sandboxing.Tests;

public class WasmSandboxTests
{
    private readonly Mock<IWasmScriptRunner> _runner = new();
    private const string Script = "import sys, json; print(json.dumps({'pass':True,'violations':[]}))";
    private const string Input = "{\"code\":\"x=1\",\"language\":\"python\",\"rule_id\":\"r1\",\"user_message\":\"hello\"}";

    [Fact]
    public async Task ExecuteAsync_ScriptSucceeds_ReturnsCorrectResult()
    {
        _runner.Setup(r => r.RunAsync(Script, Input, It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("{\"pass\":true,\"violations\":[]}", string.Empty, 0, false));

        var sandbox = new WasmSandbox(_runner.Object, NullLogger<WasmSandbox>.Instance);

        var result = await sandbox.ExecuteAsync(Script, Input, cancellationToken: CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.TimedOut.Should().BeFalse();
        result.Success.Should().BeTrue();
        result.Stdout.Should().Contain("pass");
    }

    [Fact]
    public async Task ExecuteAsync_ScriptTimesOut_ReturnsTimedOutResult()
    {
        _runner.Setup(r => r.RunAsync(Script, Input, It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, "timeout", 1, true));

        var sandbox = new WasmSandbox(_runner.Object, NullLogger<WasmSandbox>.Instance);

        var result = await sandbox.ExecuteAsync(Script, Input, cancellationToken: CancellationToken.None);

        result.TimedOut.Should().BeTrue();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var sandbox = new WasmSandbox(_runner.Object, NullLogger<WasmSandbox>.Instance);

        var act = async () => await sandbox.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void WasmSandboxFactory_SandboxType_IsWasm()
    {
        var factory = new WasmSandboxFactory(_runner.Object, NullLogger<WasmSandbox>.Instance);

        factory.SandboxType.Should().Be("wasm");
    }

    [Fact]
    public async Task WasmSandboxFactory_CreateAsync_ReturnsWasmSandbox()
    {
        var factory = new WasmSandboxFactory(_runner.Object, NullLogger<WasmSandbox>.Instance);

        var sandbox = await factory.CreateAsync(CancellationToken.None);

        sandbox.Should().BeOfType<WasmSandbox>();
    }

    [Fact]
    public async Task WasmSandboxFactory_ExecuteValidator_ReturnsOutput()
    {
        const string expectedOutput = "{\"pass\":true,\"violations\":[]}";
        _runner.Setup(r => r.RunAsync(Script, Input, It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((expectedOutput, string.Empty, 0, false));

        var factory = new WasmSandboxFactory(_runner.Object, NullLogger<WasmSandbox>.Instance);
        await using var sandbox = await factory.CreateAsync(CancellationToken.None);

        var result = await sandbox.ExecuteAsync(Script, Input, cancellationToken: CancellationToken.None);

        result.Stdout.Should().Be(expectedOutput);
        result.Success.Should().BeTrue();
    }
}
