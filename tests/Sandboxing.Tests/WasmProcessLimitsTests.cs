using Edda.Sandboxing.Wasm;

namespace Edda.Sandboxing.Tests;

/// <summary>
/// Unit tests for <see cref="WasmProcessLimits"/>: the Linux <c>ulimit</c> wrapper vs. direct launch, and
/// the output-size cap that bounds a runaway script's memory footprint.
/// </summary>
public sealed class WasmProcessLimitsTests
{
    [Fact]
    public void BuildStartInfo_Linux_WrapsWithUlimitAndExecsPython()
    {
        var info = WasmProcessLimits.BuildStartInfo("/tmp/script.py", timeoutSeconds: 7, isLinux: true);

        info.FileName.Should().Be("/bin/sh");
        info.Arguments.Should().Contain("ulimit -t 7");
        info.Arguments.Should().Contain($"ulimit -f {WasmProcessLimits.MaxFileSizeBlocks}");
        info.Arguments.Should().Contain($"ulimit -v {WasmProcessLimits.MaxVirtualMemoryKb}");
        info.Arguments.Should().Contain("exec python3 '/tmp/script.py'");
    }

    [Fact]
    public void BuildStartInfo_Linux_ClampsCpuLimitToAtLeastOneSecond()
    {
        var info = WasmProcessLimits.BuildStartInfo("/tmp/s.py", timeoutSeconds: 0, isLinux: true);

        info.Arguments.Should().Contain("ulimit -t 1");
    }

    [Fact]
    public void BuildStartInfo_NonLinux_LaunchesPythonDirectlyWithoutUlimit()
    {
        var info = WasmProcessLimits.BuildStartInfo("/tmp/script.py", timeoutSeconds: 7, isLinux: false);

        info.FileName.Should().Be("python3");
        info.Arguments.Should().Be("\"/tmp/script.py\"");
        info.Arguments.Should().NotContain("ulimit");
    }

    [Fact]
    public void BuildStartInfo_RedirectsStreamsAndHidesWindow()
    {
        var info = WasmProcessLimits.BuildStartInfo("/tmp/s.py", timeoutSeconds: 5, isLinux: false);

        info.RedirectStandardInput.Should().BeTrue();
        info.RedirectStandardOutput.Should().BeTrue();
        info.RedirectStandardError.Should().BeTrue();
        info.UseShellExecute.Should().BeFalse();
        info.CreateNoWindow.Should().BeTrue();
    }

    [Fact]
    public async Task ReadCappedAsync_UnderCap_ReturnsFullTextAndNotExceeded()
    {
        using var reader = new StringReader("hello world");

        var (text, exceeded) = await WasmProcessLimits.ReadCappedAsync(reader, maxChars: 100, CancellationToken.None);

        text.Should().Be("hello world");
        exceeded.Should().BeFalse();
    }

    [Fact]
    public async Task ReadCappedAsync_OverCap_TruncatesAndFlagsExceeded()
    {
        using var reader = new StringReader(new string('a', 5000));

        var (text, exceeded) = await WasmProcessLimits.ReadCappedAsync(reader, maxChars: 1000, CancellationToken.None);

        text.Length.Should().Be(1000);
        exceeded.Should().BeTrue();
    }

    [Fact]
    public async Task ReadCappedAsync_ExactlyCap_NotExceeded()
    {
        using var reader = new StringReader(new string('a', 1000));

        var (text, exceeded) = await WasmProcessLimits.ReadCappedAsync(reader, maxChars: 1000, CancellationToken.None);

        text.Length.Should().Be(1000);
        exceeded.Should().BeFalse();
    }
}
