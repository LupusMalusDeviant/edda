using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Creates isolated execution environments for TDK validator scripts.
/// Two implementations: DockerSandboxFactory and WasmSandboxFactory.
/// Selection is controlled by the TDK_SANDBOX_TYPE=docker|wasm environment variable.
/// </summary>
public interface ISandboxFactory
{
    /// <summary>
    /// Creates a new sandbox instance. Must be disposed after use via DisposeAsync().
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A ready-to-use sandbox instance.</returns>
    Task<ISandbox> CreateAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Identifies the sandbox type for logging and diagnostics (e.g. "docker", "wasm").</summary>
    string SandboxType { get; }
}

/// <summary>
/// A single isolated execution environment for running a TDK validator script.
/// Must be disposed after use to release container or runtime resources.
/// </summary>
public interface ISandbox : IAsyncDisposable
{
    /// <summary>
    /// Executes a Python validator script with JSON input.
    /// The sandbox enforces a 10-second execution timeout.
    /// </summary>
    /// <param name="scriptContent">Python source code of the validator.</param>
    /// <param name="jsonInput">JSON-encoded input passed to the validator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured output, exit code, and timeout status.</returns>
    Task<SandboxResult> ExecuteAsync(
        string scriptContent,
        string jsonInput,
        CancellationToken cancellationToken = default);
}
