using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Security.Configuration;

/// <summary>
/// JSON-file-backed implementation of <see cref="ISettingsService"/>. Persists non-secret
/// application settings to <c>data/settings.json</c> via <see cref="IFileSystem"/> and keeps an
/// in-memory snapshot so consumers can read the current configuration without a restart.
/// All write operations are serialized via a <see cref="SemaphoreSlim"/> to prevent file corruption.
/// </summary>
public sealed class FileSettingsService : ISettingsService
{
    private const string SettingsFilePath = "data/settings.json";
    private const string DataDirectory = "data";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fileSystem;
    private readonly ILogger<FileSettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private EddaSettings _current = new();

    /// <summary>
    /// Initializes a new instance of <see cref="FileSettingsService"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracted filesystem for all I/O.</param>
    /// <param name="logger">Logger for operational diagnostics.</param>
    public FileSettingsService(IFileSystem fileSystem, ILogger<FileSettingsService> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    /// <inheritdoc />
    public EddaSettings Current => _current;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task<EddaSettings> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }

        RaiseChanged();
        return _current;
    }

    /// <inheritdoc />
    public async Task SaveAsync(EddaSettings settings, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            _fileSystem.EnsureDirectoryExists(DataDirectory);
            await _fileSystem.WriteAllTextAsync(SettingsFilePath, json, cancellationToken).ConfigureAwait(false);
            _current = settings;
            _logger.LogInformation("Application settings saved to {Path}", SettingsFilePath);
        }
        finally
        {
            _lock.Release();
        }

        RaiseChanged();
    }

    /// <summary>
    /// Reads and deserializes the settings file, returning defaults if it is absent or corrupt.
    /// </summary>
    private async Task<EddaSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(SettingsFilePath))
        {
            return new EddaSettings();
        }

        try
        {
            var json = await _fileSystem.ReadAllTextAsync(SettingsFilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<EddaSettings>(json, JsonOptions) ?? new EddaSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Settings file at {Path} is corrupt; falling back to defaults", SettingsFilePath);
            return new EddaSettings();
        }
    }

    /// <summary>
    /// Raises the <see cref="Changed"/> event.
    /// </summary>
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
