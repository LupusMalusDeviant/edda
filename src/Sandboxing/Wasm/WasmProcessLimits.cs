using System.Diagnostics;
using System.Text;

namespace Edda.Sandboxing.Wasm;

/// <summary>
/// Resource-limit helpers for the WASM/subprocess script runner (<see cref="DefaultWasmScriptRunner"/>).
/// These are best-effort DoS backstops for the non-Docker path: a hard wall-clock kill (enforced by the
/// runner), a lowered process priority, an output-size cap, and — on Linux — <c>ulimit</c> limits on CPU
/// time, file size and address space. Full isolation (memory/CPU cgroups, no network) requires the Docker
/// sandbox; the residual risk is documented in <c>docs/tdk.md</c>.
/// </summary>
internal static class WasmProcessLimits
{
    /// <summary>Maximum number of characters captured from stdout/stderr each; bounds memory against runaway output.</summary>
    public const int MaxOutputChars = 1_000_000;

    /// <summary>Linux <c>ulimit -f</c> value (512-byte blocks): 20480 × 512 B = 10 MB max file size the script may write.</summary>
    public const int MaxFileSizeBlocks = 20_480;

    /// <summary>Linux <c>ulimit -v</c> value (KiB): 1 GiB address-space ceiling, a backstop against runaway allocation.</summary>
    public const int MaxVirtualMemoryKb = 1_048_576;

    /// <summary>
    /// Builds the <see cref="ProcessStartInfo"/> for running the Python script. On Linux the script is
    /// launched through <c>/bin/sh</c> with <c>ulimit</c> backstops (CPU time ≈ the wall-clock timeout,
    /// file size, address space); on other platforms <c>python3</c> is launched directly because
    /// <c>ulimit</c> is not available.
    /// </summary>
    /// <param name="scriptPath">Path to the temporary Python script file.</param>
    /// <param name="timeoutSeconds">Wall-clock timeout; also used as the CPU-time ceiling on Linux.</param>
    /// <param name="isLinux">Whether the host is Linux (pass <see cref="OperatingSystem.IsLinux"/> in production).</param>
    /// <returns>A start info configured for redirected, windowless execution.</returns>
    public static ProcessStartInfo BuildStartInfo(string scriptPath, int timeoutSeconds, bool isLinux)
    {
        var info = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (isLinux)
        {
            var cpuSeconds = Math.Max(1, timeoutSeconds);
            info.FileName = "/bin/sh";
            info.Arguments =
                $"-c \"ulimit -t {cpuSeconds}; ulimit -f {MaxFileSizeBlocks}; ulimit -v {MaxVirtualMemoryKb}; " +
                $"exec python3 '{scriptPath}'\"";
        }
        else
        {
            info.FileName = "python3";
            info.Arguments = $"\"{scriptPath}\"";
        }

        return info;
    }

    /// <summary>
    /// Reads up to <paramref name="maxChars"/> characters from <paramref name="reader"/>. Stops early once
    /// the cap is exceeded so a runaway script cannot exhaust host memory through its output stream.
    /// </summary>
    /// <param name="reader">The stream reader to drain.</param>
    /// <param name="maxChars">The maximum number of characters to retain.</param>
    /// <param name="ct">Cancellation token (e.g. the wall-clock timeout).</param>
    /// <returns>The captured text and whether the cap was exceeded (output truncated).</returns>
    public static async Task<(string Text, bool Exceeded)> ReadCappedAsync(
        TextReader reader, int maxChars, CancellationToken ct)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var exceeded = false;

        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                var room = maxChars - builder.Length;
                if (read <= room)
                {
                    builder.Append(buffer, 0, read);
                }
                else
                {
                    if (room > 0)
                        builder.Append(buffer, 0, room);
                    exceeded = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out or cancelled — return whatever was captured so far.
        }

        return (builder.ToString(), exceeded);
    }
}
