using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Registry of coding CLI tools available on the host system.
/// Combines user configuration with live binary detection and credential status.
/// Backed by data/coding-cli-config.json + ICredentialStore.
/// </summary>
public interface ICodingCliRegistry
{
    /// <summary>
    /// Returns the status of all known coding CLI tools, including installation
    /// detection and whether an API key is stored.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Status for each known tool.</returns>
    Task<IReadOnlyList<CodingCliStatus>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists updated settings for a single tool. If <paramref name="apiKey"/> is
    /// non-empty it is stored encrypted in ICredentialStore under the tool's credential key.
    /// Passing null or empty for <paramref name="apiKey"/> leaves an existing key unchanged.
    /// </summary>
    /// <param name="entry">The updated configuration entry.</param>
    /// <param name="apiKey">Plaintext API key to store, or null/empty to leave unchanged.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(CodingCliEntry entry, string? apiKey, CancellationToken ct = default);
}
