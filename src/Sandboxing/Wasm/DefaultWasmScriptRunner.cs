using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Wasm;

/// <summary>
/// Production implementation of <see cref="IWasmScriptRunner"/> that executes Python scripts
/// via a local <c>python3</c> subprocess. Used when <c>TDK_SANDBOX_TYPE=wasm</c>.
/// This provides a lightweight alternative to Docker-based sandboxing without container isolation.
/// </summary>
public sealed class DefaultWasmScriptRunner : IWasmScriptRunner
{
    private readonly ILogger<DefaultWasmScriptRunner> _logger;

    /// <summary>
    /// Initializes a new <see cref="DefaultWasmScriptRunner"/>.
    /// </summary>
    /// <param name="logger">Structured logger.</param>
    public DefaultWasmScriptRunner(ILogger<DefaultWasmScriptRunner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(string Stdout, string Stderr, int ExitCode, bool TimedOut)> RunAsync(
        string scriptContent,
        string jsonInput,
        int timeoutSeconds,
        CancellationToken ct)
    {
        var scriptPath = System.IO.Path.GetTempFileName() + ".py";
        try
        {
            await System.IO.File.WriteAllTextAsync(scriptPath, scriptContent, Encoding.UTF8, ct);

            using var process = new Process();
            process.StartInfo = WasmProcessLimits.BuildStartInfo(scriptPath, timeoutSeconds, OperatingSystem.IsLinux());

            process.Start();
            TrySetLowPriority(process);

            await process.StandardInput.WriteAsync(jsonInput.AsMemory(), ct);
            process.StandardInput.Close();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            // Capped reads bound the memory a runaway script can consume through its output streams.
            var readStdout = WasmProcessLimits.ReadCappedAsync(process.StandardOutput, WasmProcessLimits.MaxOutputChars, cts.Token);
            var readStderr = WasmProcessLimits.ReadCappedAsync(process.StandardError, WasmProcessLimits.MaxOutputChars, cts.Token);

            bool killed = false;
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                killed = true;
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }

            var (stdout, stdoutExceeded) = await readStdout;
            var (stderr, stderrExceeded) = await readStderr;
            var outputExceeded = stdoutExceeded || stderrExceeded;

            // An output overflow that did not also trip the wall-clock timeout still terminates the run.
            if (outputExceeded && !killed)
            {
                killed = true;
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }

            if (killed)
            {
                _logger.LogWarning(
                    "WasmSandbox script terminated: {Reason} (timeout {Seconds}s, output cap {Cap} chars)",
                    outputExceeded ? "output limit exceeded" : "wall-clock timeout",
                    timeoutSeconds, WasmProcessLimits.MaxOutputChars);
                return (stdout, stderr, 1, true);
            }

            _logger.LogDebug("WasmSandbox script completed exitCode={ExitCode}", process.ExitCode);
            return (stdout, stderr, process.ExitCode, false);
        }
        finally
        {
            try { System.IO.File.Delete(scriptPath); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>
    /// Lowers the process priority to <see cref="ProcessPriorityClass.BelowNormal"/> so a CPU-heavy script
    /// cannot starve the host. Best-effort: some platforms or an already-exited process may reject it.
    /// </summary>
    /// <param name="process">The started process.</param>
    private void TrySetLowPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not lower WasmSandbox process priority");
        }
    }
}
