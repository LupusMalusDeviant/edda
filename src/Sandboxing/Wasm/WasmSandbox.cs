using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Wasm;

/// <summary>
/// A single-use WASM-based sandbox for executing Python scripts without Docker.
/// Created by <see cref="WasmSandboxFactory"/>. Uses a local Python subprocess.
/// </summary>
public sealed class WasmSandbox : ISandbox
{
    private const int DefaultTimeoutSeconds = 10;

    private readonly IWasmScriptRunner _runner;
    private readonly ILogger<WasmSandbox> _logger;

    /// <summary>
    /// Initializes a new <see cref="WasmSandbox"/>.
    /// </summary>
    /// <param name="runner">The script runner that executes Python code.</param>
    /// <param name="logger">Structured logger.</param>
    public WasmSandbox(IWasmScriptRunner runner, ILogger<WasmSandbox> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SandboxResult> ExecuteAsync(
        string scriptContent,
        string jsonInput,
        IReadOnlyDictionary<string, string>? additionalFiles = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("WasmSandbox executing script (length={Length} chars)", scriptContent.Length);

        var (stdout, stderr, exitCode, timedOut) = await _runner.RunAsync(
            scriptContent,
            jsonInput,
            DefaultTimeoutSeconds,
            additionalFiles,
            cancellationToken);

        if (timedOut)
            _logger.LogWarning("WasmSandbox script timed out after {Seconds}s", DefaultTimeoutSeconds);
        else
            _logger.LogDebug("WasmSandbox script completed exitCode={ExitCode}", exitCode);

        return new SandboxResult
        {
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
            TimedOut = timedOut
        };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
