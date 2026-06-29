using Edda.Sandboxing.Docker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Sandboxing.Tests;

public class DotNetBuildSandboxTests
{
    private readonly Mock<IDotNetBuildContainerOps> _ops = new();
    private const string ContainerId = "abc123def456789";

    private DotNetBuildSandbox CreateSut() =>
        new(_ops.Object, ContainerId, NullLogger<DotNetBuildSandbox>.Instance);

    [Fact]
    public async Task CopySourceAsync_WritesAllFilesToContainer()
    {
        _ops.Setup(o => o.ExecAsync(ContainerId, It.IsAny<string[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, string.Empty, 0, false));
        _ops.Setup(o => o.CopyFileAsync(ContainerId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var files = new Dictionary<string, string>
        {
            ["MyPlugin/MyPlugin.csproj"] = "<Project />",
            ["MyPlugin/MyTool.cs"] = "class MyTool {}",
            ["MyPlugin/manifest.json"] = "{}"
        };

        var sut = CreateSut();
        await sut.CopySourceAsync(files);

        // Verify mkdir for each file's parent directory
        _ops.Verify(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("mkdir -p")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Verify each file is copied
        _ops.Verify(o => o.CopyFileAsync(
            ContainerId,
            It.Is<string>(p => p == "/src/MyPlugin/MyPlugin.csproj"),
            "<Project />",
            It.IsAny<CancellationToken>()),
            Times.Once);

        _ops.Verify(o => o.CopyFileAsync(
            ContainerId,
            It.Is<string>(p => p == "/src/MyPlugin/MyTool.cs"),
            "class MyTool {}",
            It.IsAny<CancellationToken>()),
            Times.Once);

        _ops.Verify(o => o.CopyFileAsync(
            ContainerId,
            It.Is<string>(p => p == "/src/MyPlugin/manifest.json"),
            "{}",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BuildAsync_SuccessfulBuild_ReturnsExitCodeZero()
    {
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("dotnet build")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Build succeeded.", string.Empty, 0, false));

        var sut = CreateSut();
        var result = await sut.BuildAsync("MyPlugin");

        result.ExitCode.Should().Be(0);
        result.TimedOut.Should().BeFalse();
        result.Success.Should().BeTrue();
        result.Stdout.Should().Contain("Build succeeded.");
    }

    [Fact]
    public async Task BuildAsync_CompilationError_ReturnsNonZeroExitCode()
    {
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("dotnet build")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, "error CS1002: ; expected", 1, false));

        var sut = CreateSut();
        var result = await sut.BuildAsync("MyPlugin");

        result.ExitCode.Should().Be(1);
        result.Success.Should().BeFalse();
        result.Stderr.Should().Contain("CS1002");
    }

    [Fact]
    public async Task BuildAsync_Timeout_ReturnsTimedOut()
    {
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("dotnet build")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, string.Empty, 1, true));

        var sut = CreateSut();
        var result = await sut.BuildAsync("MyPlugin");

        result.TimedOut.Should().BeTrue();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_ExecutesInCorrectProjectDirectory()
    {
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.IsAny<string[]>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, string.Empty, 0, false));

        var sut = CreateSut();
        await sut.BuildAsync("WeatherTool");

        _ops.Verify(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd =>
                cmd[2].Contains("cd /src/WeatherTool") &&
                cmd[2].Contains("dotnet build -c Release --nologo")),
            120,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_ReturnsDllAndDepsJsonBytes()
    {
        var dllBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00 }; // MZ header stub
        var depsBytes = new byte[] { 0x7B, 0x7D }; // {}
        var dllBase64 = Convert.ToBase64String(dllBytes);
        var depsBase64 = Convert.ToBase64String(depsBytes);

        // ls command returns file listing
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("ls -1")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("MyPlugin.dll\nMyPlugin.deps.json\nMyPlugin.pdb\nMyPlugin.runtimeconfig.json\n", string.Empty, 0, false));

        // base64 reads for DLL
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("base64") && cmd[2].Contains("MyPlugin.dll")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((dllBase64, string.Empty, 0, false));

        // base64 reads for deps.json
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("base64") && cmd[2].Contains("MyPlugin.deps.json")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((depsBase64, string.Empty, 0, false));

        var sut = CreateSut();
        var artifacts = await sut.ExtractArtifactsAsync("MyPlugin");

        artifacts.Should().HaveCount(2);
        artifacts.Should().ContainKey("MyPlugin.dll");
        artifacts.Should().ContainKey("MyPlugin.deps.json");
        artifacts["MyPlugin.dll"].Should().BeEquivalentTo(dllBytes);
        artifacts["MyPlugin.deps.json"].Should().BeEquivalentTo(depsBytes);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_NoOutputDir_ReturnsEmptyDictionary()
    {
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("ls -1")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, "No such file or directory", 1, false));

        var sut = CreateSut();
        var artifacts = await sut.ExtractArtifactsAsync("MyPlugin");

        artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractArtifactsAsync_SkipsPdbAndRuntimeConfig()
    {
        _ops.Setup(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("ls -1")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("MyPlugin.pdb\nMyPlugin.runtimeconfig.json\n", string.Empty, 0, false));

        var sut = CreateSut();
        var artifacts = await sut.ExtractArtifactsAsync("MyPlugin");

        artifacts.Should().BeEmpty();
        // Verify no base64 reads happened
        _ops.Verify(o => o.ExecAsync(
            ContainerId,
            It.Is<string[]>(cmd => cmd[2].Contains("base64")),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_StopsContainer()
    {
        _ops.Setup(o => o.StopAsync(ContainerId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.DisposeAsync();

        _ops.Verify(o => o.StopAsync(ContainerId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ContainerAlreadyGone_DoesNotThrow()
    {
        _ops.Setup(o => o.StopAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("container not found"));

        var sut = CreateSut();

        var act = () => sut.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }
}
