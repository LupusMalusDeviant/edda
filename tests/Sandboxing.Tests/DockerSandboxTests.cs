using Edda.Core.Models;
using Edda.Sandboxing.Docker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Sandboxing.Tests;

public class DockerSandboxTests
{
    private readonly Mock<IDockerContainerOperations> _ops = new();
    private const string ContainerId = "abc123def456";
    private const string Script = "import sys, json; data=json.load(sys.stdin); print(json.dumps({'pass':True,'violations':[]}))";
    private const string Input = "{\"code\":\"x=1\",\"language\":\"python\",\"rule_id\":\"r1\",\"user_message\":\"hello\"}";

    [Fact]
    public async Task ExecuteAsync_ScriptSucceeds_ReturnsCorrectResult()
    {
        _ops.Setup(o => o.CopyFileAsync(ContainerId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ops.Setup(o => o.ExecAsync(ContainerId, It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("{\"pass\":true,\"violations\":[]}", string.Empty, 0, false));

        var sandbox = new DockerSandbox(_ops.Object, ContainerId, NullLogger<DockerSandbox>.Instance);

        var result = await sandbox.ExecuteAsync(Script, Input, CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.TimedOut.Should().BeFalse();
        result.Success.Should().BeTrue();
        result.Stdout.Should().Contain("pass");
    }

    [Fact]
    public async Task ExecuteAsync_ScriptTimesOut_ReturnsTimedOutResult()
    {
        _ops.Setup(o => o.CopyFileAsync(ContainerId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ops.Setup(o => o.ExecAsync(ContainerId, It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, "timeout", 1, true));

        var sandbox = new DockerSandbox(_ops.Object, ContainerId, NullLogger<DockerSandbox>.Instance);

        var result = await sandbox.ExecuteAsync(Script, Input, CancellationToken.None);

        result.TimedOut.Should().BeTrue();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CopiesScriptAndInput()
    {
        _ops.Setup(o => o.CopyFileAsync(ContainerId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ops.Setup(o => o.ExecAsync(ContainerId, It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, string.Empty, 0, false));

        var sandbox = new DockerSandbox(_ops.Object, ContainerId, NullLogger<DockerSandbox>.Instance);

        await sandbox.ExecuteAsync(Script, Input, CancellationToken.None);

        _ops.Verify(o => o.CopyFileAsync(ContainerId, "/workspace/script.py", Script, It.IsAny<CancellationToken>()), Times.Once);
        _ops.Verify(o => o.CopyFileAsync(ContainerId, "/workspace/input.json", Input, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_StopsContainer()
    {
        _ops.Setup(o => o.StopContainerAsync(ContainerId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sandbox = new DockerSandbox(_ops.Object, ContainerId, NullLogger<DockerSandbox>.Instance);

        await sandbox.DisposeAsync();

        _ops.Verify(o => o.StopContainerAsync(ContainerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DockerSandboxFactory_SandboxType_IsDocker()
    {
        var factory = new DockerSandboxFactory(_ops.Object, NullLogger<DockerSandbox>.Instance);

        factory.SandboxType.Should().Be("docker");
    }

    [Fact]
    public async Task DockerSandboxFactory_CreateAsync_ReturnsDockerSandbox()
    {
        _ops.Setup(o => o.CreateAndStartContainerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContainerId);

        var factory = new DockerSandboxFactory(_ops.Object, NullLogger<DockerSandbox>.Instance);

        var sandbox = await factory.CreateAsync(CancellationToken.None);

        sandbox.Should().BeOfType<DockerSandbox>();
    }

    [Fact]
    public void DefaultDockerContainerOperations_DefaultImage_IsPython312Slim()
    {
        DefaultDockerContainerOperations.DefaultPythonImage.Should().Be("python:3.12-slim");
    }

    [Fact]
    public void DefaultDockerContainerOperations_CustomImage_IsAccepted()
    {
        // Verify that custom image parameter is accepted without throwing
        var act = () => new DefaultDockerContainerOperations(
            NullLogger<DefaultDockerContainerOperations>.Instance, "python:latest");

        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultDockerContainerOperations_NullImage_FallsBackToDefault()
    {
        // Verify that null image parameter is accepted without throwing (uses default)
        var act = () => new DefaultDockerContainerOperations(
            NullLogger<DefaultDockerContainerOperations>.Instance, null);

        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultDockerContainerOperations_NetworkModeNone_IsAccepted()
    {
        var act = () => new DefaultDockerContainerOperations(
            NullLogger<DefaultDockerContainerOperations>.Instance,
            networkMode: DefaultDockerContainerOperations.NetworkNone);

        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultDockerContainerOperations_NetworkModeBridge_IsAccepted()
    {
        var act = () => new DefaultDockerContainerOperations(
            NullLogger<DefaultDockerContainerOperations>.Instance,
            networkMode: DefaultDockerContainerOperations.NetworkBridge);

        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultDockerContainerOperations_NetworkConstants_HaveExpectedValues()
    {
        DefaultDockerContainerOperations.NetworkNone.Should().Be("none");
        DefaultDockerContainerOperations.NetworkBridge.Should().Be("bridge");
    }
}
