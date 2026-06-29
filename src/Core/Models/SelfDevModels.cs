namespace Edda.Core.Models;

/// <summary>
/// Specification for a plugin to be developed by the self-development pipeline.
/// Describes the desired tool behavior, dependencies, and naming.
/// </summary>
public sealed record PluginSpec
{
    /// <summary>
    /// Desired plugin name in kebab-case (e.g. "weather-tool").
    /// Used as directory name and manifest name.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// Natural language description of what the plugin should do.
    /// This is sent to the LLM as the primary specification.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Tool name in snake_case (e.g. "get_weather").
    /// If null, derived from <see cref="PluginName"/> by replacing hyphens with underscores.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Optional additional context or constraints for the LLM code generation.
    /// </summary>
    public string? AdditionalContext { get; init; }

    /// <summary>
    /// Required credentials the plugin needs (keys in <c>ICredentialStore</c>).
    /// Used to generate constructor dependencies in the generated code.
    /// </summary>
    public IReadOnlyList<string> RequiredCredentials { get; init; } = [];

    /// <summary>
    /// Optional NuGet packages the plugin needs beyond Edda.Core.
    /// Format: "PackageName/Version" (e.g. "Newtonsoft.Json/13.0.3").
    /// </summary>
    public IReadOnlyList<string> NuGetPackages { get; init; } = [];
}

/// <summary>
/// Result of the self-development pipeline.
/// </summary>
public sealed record SelfDevResult
{
    /// <summary>Whether the plugin was successfully built and deployed.</summary>
    public required bool Success { get; init; }

    /// <summary>Plugin name as deployed.</summary>
    public required string PluginName { get; init; }

    /// <summary>Names of tools registered by the new plugin.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Build output (stdout + stderr) for diagnostics.</summary>
    public string? BuildOutput { get; init; }

    /// <summary>Error message if the pipeline failed.</summary>
    public string? Error { get; init; }

    /// <summary>Which step failed: "generation", "build", "validation", "deployment".</summary>
    public string? FailedStep { get; init; }

    /// <summary>The generated source code (for debugging and review).</summary>
    public string? GeneratedSourceCode { get; init; }
}

/// <summary>
/// Result of a <c>dotnet build</c> execution inside the build sandbox container.
/// </summary>
public sealed record BuildSandboxResult
{
    /// <summary>Process exit code (0 = success).</summary>
    public required int ExitCode { get; init; }

    /// <summary>Standard output from dotnet build.</summary>
    public required string Stdout { get; init; }

    /// <summary>Standard error from dotnet build.</summary>
    public required string Stderr { get; init; }

    /// <summary>Whether the build timed out before completion.</summary>
    public required bool TimedOut { get; init; }

    /// <summary>True if <see cref="ExitCode"/> is 0 and the build did not time out.</summary>
    public bool Success => ExitCode == 0 && !TimedOut;
}
