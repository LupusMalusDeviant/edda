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
        IReadOnlyDictionary<string, string>? additionalFiles = null,
        CancellationToken ct = default)
    {
        // With companion files (e.g. the tdk.py helper) run from a dedicated temp directory so the
        // script can import them: python3 places the script's own directory on sys.path. Without
        // companion files, keep the original single-temp-file behavior exactly.
        string scriptPath;
        string? workDir = null;
        if (additionalFiles is { Count: > 0 })
        {
            workDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "edda-tdk-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(workDir);
            scriptPath = System.IO.Path.Combine(workDir, "script.py");
        }
        else
        {
            scriptPath = System.IO.Path.GetTempFileName() + ".py";
        }

        try
        {
            await System.IO.File.WriteAllTextAsync(scriptPath, scriptContent, Encoding.UTF8, ct);

            if (workDir is not null)
            {
                foreach (var (name, content) in additionalFiles!)
                {
                    var safeName = System.IO.Path.GetFileName(name);
                    if (string.IsNullOrEmpty(safeName))
                        continue;
                    await System.IO.File.WriteAllTextAsync(
                        System.IO.Path.Combine(workDir, safeName), content, Encoding.UTF8, ct);
                }
            }

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
            try
            {
                if (workDir is not null)
                    System.IO.Directory.Delete(workDir, recursive: true);
                else
                    System.IO.File.Delete(scriptPath);
            }
            catch { /* best effort cleanup */ }
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
