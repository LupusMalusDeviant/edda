using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Sandboxing;

namespace Edda.Sandboxing.Tests;

public class NullSandboxFactoryTests
{
    private readonly NullSandboxFactory _sut = new();

    [Fact]
    public void NullSandboxFactory_SandboxType_IsNull()
    {
        _sut.SandboxType.Should().Be("null");
    }

    [Fact]
    public async Task NullSandboxFactory_CreateAsync_ReturnsNullSandbox()
    {
        var sandbox = await _sut.CreateAsync();

        sandbox.Should().NotBeNull();
        sandbox.Should().BeOfType<NullSandbox>();
    }

    [Fact]
    public async Task NullSandbox_ExecuteAsync_ReturnsUnconfiguredError()
    {
        await using var sandbox = await _sut.CreateAsync();

        var result = await sandbox.ExecuteAsync("print('hello')", "{}", CancellationToken.None);

        result.ExitCode.Should().Be(1);
        result.Stdout.Should().BeEmpty();
        result.Stderr.Should().Contain("not configured");
        result.TimedOut.Should().BeFalse();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task NullSandbox_DisposeAsync_DoesNotThrow()
    {
        var sandbox = await _sut.CreateAsync();

        var act = async () => await sandbox.DisposeAsync();

        await act.Should().NotThrowAsync();
    }
}
