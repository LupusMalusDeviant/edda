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
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            await process.StandardInput.WriteAsync(jsonInput);
            process.StandardInput.Close();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            string stdout = string.Empty;
            string stderr = string.Empty;
            bool timedOut = false;

            try
            {
                var readStdout = process.StandardOutput.ReadToEndAsync(cts.Token);
                var readStderr = process.StandardError.ReadToEndAsync(cts.Token);

                await process.WaitForExitAsync(cts.Token);
                stdout = await readStdout;
                stderr = await readStderr;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                timedOut = true;
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                _logger.LogWarning("WasmSandbox script timed out after {Seconds}s", timeoutSeconds);
            }

            _logger.LogDebug("WasmSandbox script completed exitCode={ExitCode}", process.ExitCode);
            return (stdout, stderr, timedOut ? 1 : process.ExitCode, timedOut);
        }
        finally
        {
            try { System.IO.File.Delete(scriptPath); } catch { /* best effort cleanup */ }
        }
    }
}
