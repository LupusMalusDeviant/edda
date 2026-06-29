using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Edda.Sandboxing.Wasm;

/// <summary>
/// Creates <see cref="WasmSandbox"/> instances backed by local Python subprocess execution.
/// Used when <c>TDK_SANDBOX_TYPE=wasm</c> is configured.
/// Does not require Docker but provides less isolation than <c>DockerSandboxFactory</c>.
/// </summary>
public sealed class WasmSandboxFactory : ISandboxFactory
{
    private readonly IWasmScriptRunner _runner;
    private readonly ILogger<WasmSandbox> _sandboxLogger;

    /// <inheritdoc />
    public string SandboxType => "wasm";

    /// <summary>
    /// Initializes a new <see cref="WasmSandboxFactory"/>.
    /// </summary>
    /// <param name="runner">The script runner injected into each created sandbox.</param>
    /// <param name="sandboxLogger">Logger forwarded to each created <see cref="WasmSandbox"/>.</param>
    public WasmSandboxFactory(IWasmScriptRunner runner, ILogger<WasmSandbox> sandboxLogger)
    {
        _runner = runner;
        _sandboxLogger = sandboxLogger;
    }

    /// <inheritdoc />
    public Task<ISandbox> CreateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ISandbox>(new WasmSandbox(_runner, _sandboxLogger));
}
