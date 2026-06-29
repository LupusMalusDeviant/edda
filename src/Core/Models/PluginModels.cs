namespace Edda.Core.Models;

/// <summary>
/// Declares a .NET DLL plugin: which assembly file to load and which tool types to instantiate.
/// Stored as <c>{name}.manifest.json</c> alongside the plugin DLL in the plugins directory.
/// </summary>
/// <remarks>
/// Example manifest:
/// <code>
/// {
///   "name": "my-plugin",
///   "version": "1.0.0",
///   "assemblyFile": "MyPlugin.dll",
///   "toolTypes": ["MyPlugin.Tools.MyFirstTool"]
/// }
/// </code>
/// If <see cref="ToolTypes"/> is omitted, all exported types implementing
/// <see cref="Edda.Core.Abstractions.IAgentTool"/> are loaded automatically.
/// </remarks>
public sealed record PluginManifest
{
    /// <summary>Unique plugin name used as the registry key and for unloading.</summary>
    public required string Name { get; init; }

    /// <summary>Version string for display and audit purposes.</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// File name of the plugin DLL, relative to the plugins directory.
    /// Example: <c>"MyPlugin.dll"</c>.
    /// </summary>
    public required string AssemblyFile { get; init; }

    /// <summary>
    /// Optional list of fully-qualified type names to instantiate as tools.
    /// When null or empty, all public non-abstract types implementing
    /// <see cref="Edda.Core.Abstractions.IAgentTool"/> are loaded.
    /// </summary>
    public IReadOnlyList<string>? ToolTypes { get; init; }
}

/// <summary>
/// Runtime information about a currently loaded plugin.
/// Returned by <see cref="Edda.Core.Abstractions.IPluginLoader.GetLoadedPlugins"/>.
/// </summary>
public sealed record PluginInfo
{
    /// <summary>Plugin name as declared in the manifest.</summary>
    public required string Name { get; init; }

    /// <summary>Plugin version as declared in the manifest.</summary>
    public required string Version { get; init; }

    /// <summary>Absolute path of the loaded assembly file.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>UTC timestamp when the plugin was loaded into the context.</summary>
    public required DateTimeOffset LoadedAt { get; init; }

    /// <summary>Names of all <see cref="Edda.Core.Abstractions.IAgentTool"/> instances registered by this plugin.</summary>
    public required IReadOnlyList<string> ToolNames { get; init; }
}
