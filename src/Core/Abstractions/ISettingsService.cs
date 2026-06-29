using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Provides access to the persisted, runtime-editable application settings (non-secret).
/// Settings are held as an in-memory snapshot and re-published on change, so consumers can resolve
/// the current configuration without a process restart. Secrets are never stored here — they belong
/// in <see cref="ICredentialStore"/>.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current in-memory settings snapshot. Never null; returns defaults until settings
    /// have been loaded via <see cref="ReloadAsync"/> or written via <see cref="SaveAsync"/>.
    /// </summary>
    EddaSettings Current { get; }

    /// <summary>
    /// Loads the settings from the backing store into memory, replacing <see cref="Current"/>.
    /// Returns default settings if no persisted settings exist yet.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The settings now held in <see cref="Current"/>.</returns>
    Task<EddaSettings> ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the given settings to the backing store, updates <see cref="Current"/>, and raises
    /// <see cref="Changed"/>.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(EddaSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised after <see cref="Current"/> changes (following a successful save or reload), so live
    /// consumers can invalidate cached, settings-derived state.
    /// </summary>
    event EventHandler? Changed;
}
