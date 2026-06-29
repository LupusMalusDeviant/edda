using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates self-development of new plugins using LLM-generated code,
/// Docker-based compilation, and automatic loading via the plugin system.
/// </summary>
public interface ISelfDevPipeline
{
    /// <summary>
    /// Develops a new plugin from a specification: generates code via LLM,
    /// builds in a .NET SDK Docker container, validates the artifact,
    /// and deploys to the plugins directory for hot-loading.
    /// </summary>
    /// <param name="spec">Plugin specification describing the desired tool.</param>
    /// <param name="userId">User requesting the plugin development.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with plugin name, tools registered, and any build output.</returns>
    Task<SelfDevResult> DevelopPluginAsync(
        PluginSpec spec,
        string userId,
        CancellationToken ct = default);
}

/// <summary>
/// Abstraction for building .NET projects inside Docker containers.
/// Used by <see cref="ISelfDevPipeline"/> to compile plugin code in isolation.
/// </summary>
public interface IDotNetBuildSandbox : IAsyncDisposable
{
    /// <summary>
    /// Copies source files into the build container.
    /// Keys are relative file paths (e.g. "MyPlugin/MyTool.cs"), values are file contents.
    /// </summary>
    /// <param name="files">Map of relative paths to file contents.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CopySourceAsync(
        IReadOnlyDictionary<string, string> files,
        CancellationToken ct = default);

    /// <summary>
    /// Runs <c>dotnet build</c> inside the container and returns the result.
    /// </summary>
    /// <param name="projectName">Name of the project directory to build.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Build result with exit code, output, and timeout status.</returns>
    Task<BuildSandboxResult> BuildAsync(
        string projectName,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts the build output (DLL, deps.json) from the container after a successful build.
    /// Keys are file names, values are the raw bytes.
    /// </summary>
    /// <param name="projectName">Name of the project whose artifacts to extract.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Map of artifact file names to their binary content.</returns>
    Task<IReadOnlyDictionary<string, byte[]>> ExtractArtifactsAsync(
        string projectName,
        CancellationToken ct = default);
}

/// <summary>
/// Factory for creating .NET SDK build sandbox containers.
/// Each sandbox is an isolated Docker container with the .NET SDK installed.
/// </summary>
public interface IDotNetBuildSandboxFactory
{
    /// <summary>
    /// Creates a new .NET SDK container ready for compilation.
    /// The container is started and ready to accept source files.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new build sandbox that must be disposed after use.</returns>
    Task<IDotNetBuildSandbox> CreateAsync(CancellationToken ct = default);
}
