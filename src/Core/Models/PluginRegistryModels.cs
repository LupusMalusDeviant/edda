namespace Edda.Core.Models;

/// <summary>
/// Root structure for the plugin-registry.json configuration file.
/// Defines all available plugins that can be installed by the agent.
/// </summary>
public sealed record PluginRegistryManifest
{
    /// <summary>Schema version of the registry format.</summary>
    public required int Version { get; init; }

    /// <summary>All available plugins in this registry.</summary>
    public required IReadOnlyList<PluginRegistryEntry> Plugins { get; init; }
}

/// <summary>
/// A single plugin entry in the curated registry.
/// Contains metadata, download URL, and integrity checksum.
/// </summary>
public sealed record PluginRegistryEntry
{
    /// <summary>Plugin name matching the manifest name (kebab-case).</summary>
    public required string Name { get; init; }

    /// <summary>Semantic version string (e.g. "1.0.0").</summary>
    public required string Version { get; init; }

    /// <summary>Human-readable description of the plugin's functionality.</summary>
    public required string Description { get; init; }

    /// <summary>HTTPS URL to download the plugin archive (.zip).</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>
    /// SHA-256 hash (hex-encoded, lowercase) of the download archive for integrity verification.
    /// Installation fails if the computed hash does not match.
    /// </summary>
    public required string Sha256 { get; init; }

    /// <summary>Credential keys required by this plugin (keys in <c>ICredentialStore</c>).</summary>
    public IReadOnlyList<string> RequiredCredentials { get; init; } = [];

    /// <summary>Author or organization name.</summary>
    public string? Author { get; init; }

    /// <summary>Whether this plugin is currently installed locally. Set at query time.</summary>
    public bool IsInstalled { get; init; }

    /// <summary>Installed version if different from registry version. Set at query time.</summary>
    public string? InstalledVersion { get; init; }
}

/// <summary>
/// Result of a plugin install or update operation from the registry.
/// </summary>
public sealed record PluginInstallResult
{
    /// <summary>Whether the operation succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Plugin name that was installed or updated.</summary>
    public required string PluginName { get; init; }

    /// <summary>Version that was installed.</summary>
    public string? Version { get; init; }

    /// <summary>Tool names registered by the plugin after loading.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Error message on failure.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Credential keys that the user still needs to configure for this plugin to function.
    /// </summary>
    public IReadOnlyList<string> MissingCredentials { get; init; } = [];
}

/// <summary>
/// Information about an available update for an installed plugin.
/// </summary>
public sealed record PluginUpdateInfo
{
    /// <summary>Plugin name.</summary>
    public required string PluginName { get; init; }

    /// <summary>Currently installed version.</summary>
    public required string InstalledVersion { get; init; }

    /// <summary>Available version from the registry.</summary>
    public required string AvailableVersion { get; init; }
}
