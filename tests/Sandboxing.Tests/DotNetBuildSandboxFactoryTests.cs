using Edda.Sandboxing.Docker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Sandboxing.Tests;

public class DotNetBuildSandboxFactoryTests
{
    private readonly Mock<IDotNetBuildContainerOps> _ops = new();
    private const string ContainerId = "abc123def456789";

    [Fact]
    public async Task CreateAsync_ReturnsRunningDotNetBuildSandbox()
    {
        _ops.Setup(o => o.CreateAndStartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContainerId);

        var factory = new DotNetBuildSandboxFactory(
            _ops.Object, NullLogger<DotNetBuildSandbox>.Instance);

        var sandbox = await factory.CreateAsync();

        sandbox.Should().BeOfType<DotNetBuildSandbox>();
    }

    [Fact]
    public async Task CreateAsync_CallsCreateAndStartOnOps()
    {
        _ops.Setup(o => o.CreateAndStartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContainerId);

        var factory = new DotNetBuildSandboxFactory(
            _ops.Object, NullLogger<DotNetBuildSandbox>.Instance);

        await factory.CreateAsync();

        _ops.Verify(o => o.CreateAndStartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_PassesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _ops.Setup(o => o.CreateAndStartAsync(cts.Token))
            .ReturnsAsync(ContainerId);

        var factory = new DotNetBuildSandboxFactory(
            _ops.Object, NullLogger<DotNetBuildSandbox>.Instance);

        await factory.CreateAsync(cts.Token);

        _ops.Verify(o => o.CreateAndStartAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_Failure_Propagates()
    {
        _ops.Setup(o => o.CreateAndStartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Docker daemon not running"));

        var factory = new DotNetBuildSandboxFactory(
            _ops.Object, NullLogger<DotNetBuildSandbox>.Instance);

        var act = () => factory.CreateAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Docker daemon not running");
    }

    [Fact]
    public void DefaultDotNetBuildContainerOps_DefaultImage_IsDotNetSdk10()
    {
        DefaultDotNetBuildContainerOps.DefaultSdkImage.Should().Be("mcr.microsoft.com/dotnet/sdk:10.0");
    }

    [Fact]
    public void DefaultDotNetBuildContainerOps_CustomImage_IsAccepted()
    {
        var act = () => new DefaultDotNetBuildContainerOps(
            NullLogger<DefaultDotNetBuildContainerOps>.Instance, "mcr.microsoft.com/dotnet/sdk:9.0");

        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultDotNetBuildContainerOps_NullImage_FallsBackToDefault()
    {
        var act = () => new DefaultDotNetBuildContainerOps(
            NullLogger<DefaultDotNetBuildContainerOps>.Instance, null);

        act.Should().NotThrow();
    }
}
