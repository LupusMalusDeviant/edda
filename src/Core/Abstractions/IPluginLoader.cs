using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Loads, reloads, and unloads .NET DLL plugins at runtime without restarting the process.
/// Each plugin assembly is isolated in its own collectible
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, allowing clean unloading.
/// Plugins are discovered via <c>.manifest.json</c> files in the configured plugins directory.
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Loads all plugins found in the configured plugins directory.
    /// Scans for <c>*.manifest.json</c> files and loads each.
    /// Safe to call multiple times — already-loaded plugins are reloaded (old context is unloaded first).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads or reloads a single plugin by its manifest file path.
    /// If the plugin is already loaded, the old <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// is unloaded and its tools are unregistered before the new version is loaded.
    /// </summary>
    /// <param name="manifestPath">Absolute path to the <c>.manifest.json</c> file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LoadPluginAsync(string manifestPath, CancellationToken ct = default);

    /// <summary>
    /// Unloads the plugin identified by <paramref name="pluginName"/>,
    /// removes its tools from the registry, and releases the
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>.
    /// No-op if the plugin is not currently loaded.
    /// </summary>
    /// <param name="pluginName">The plugin name as declared in the manifest.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnloadPluginAsync(string pluginName, CancellationToken ct = default);

    /// <summary>
    /// Returns metadata for all currently loaded plugins.
    /// </summary>
    /// <returns>An immutable snapshot of <see cref="PluginInfo"/> for each loaded plugin.</returns>
    IReadOnlyList<PluginInfo> GetLoadedPlugins();
}
