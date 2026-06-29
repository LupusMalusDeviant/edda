using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Manages a curated registry of approved plugins that can be installed from trusted sources.
/// The registry is backed by a plugin-registry.json configuration file and supports
/// optional remote registry URLs for centralized plugin distribution.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>
    /// Returns all entries from the plugin registry, enriched with installation status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All registry entries with current installation information.</returns>
    Task<IReadOnlyList<PluginRegistryEntry>> ListAvailableAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Returns a single registry entry by plugin name, or null if not found.
    /// </summary>
    /// <param name="pluginName">Plugin name in kebab-case.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The registry entry, or null if the plugin is not in the registry.</returns>
    Task<PluginRegistryEntry?> GetEntryAsync(
        string pluginName,
        CancellationToken ct = default);

    /// <summary>
    /// Downloads, verifies checksum, extracts, and installs a plugin from the registry.
    /// If the plugin is already installed, it is unloaded and replaced.
    /// </summary>
    /// <param name="pluginName">Plugin name to install.</param>
    /// <param name="userId">User requesting the installation (for audit logging).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Installation result including registered tool names and any missing credentials.</returns>
    Task<PluginInstallResult> InstallAsync(
        string pluginName,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Checks for newer versions of installed plugins against the registry.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of plugins with available updates.</returns>
    Task<IReadOnlyList<PluginUpdateInfo>> CheckForUpdatesAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Updates an installed plugin to the latest registry version.
    /// Equivalent to uninstalling the old version and installing the new one.
    /// </summary>
    /// <param name="pluginName">Plugin name to update.</param>
    /// <param name="userId">User requesting the update (for audit logging).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Installation result for the updated version.</returns>
    Task<PluginInstallResult> UpdateAsync(
        string pluginName,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Unloads and removes an installed plugin from disk.
    /// </summary>
    /// <param name="pluginName">Plugin name to remove.</param>
    /// <param name="userId">User requesting the removal (for audit logging).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the plugin was found and removed; false if not installed.</returns>
    Task<bool> RemoveAsync(
        string pluginName,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Reloads the registry from disk or remote URL, invalidating any cached state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ReloadRegistryAsync(CancellationToken ct = default);
}
