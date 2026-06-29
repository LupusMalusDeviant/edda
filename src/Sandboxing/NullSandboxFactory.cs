using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Sandboxing;

/// <summary>
/// No-op sandbox factory used until Phase 8 provides real Docker/WASM sandboxing.
/// Returns a <see cref="NullSandbox"/> that always reports "Sandboxing not configured."
/// Registered via <c>TryAddSingleton</c> so Phase 8 can override without code changes.
/// </summary>
public sealed class NullSandboxFactory : ISandboxFactory
{
    /// <inheritdoc />
    public string SandboxType => "null";

    /// <inheritdoc />
    public Task<ISandbox> CreateAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ISandbox>(new NullSandbox());
}

/// <summary>
/// No-op sandbox that returns an error result indicating sandboxing is not yet configured.
/// </summary>
public sealed class NullSandbox : ISandbox
{
    /// <inheritdoc />
    public Task<SandboxResult> ExecuteAsync(
        string scriptContent,
        string jsonInput,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SandboxResult
        {
            ExitCode = 1,
            Stdout = string.Empty,
            Stderr = "Sandboxing not configured. Enable sandboxing in Phase 8.",
            TimedOut = false
        });

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
