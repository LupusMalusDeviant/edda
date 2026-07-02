using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Sync;

/// <summary>
/// <see cref="ISyncStateStore"/> backed by JSON files under a <c>data/</c> directory via
/// <see cref="IFileSystem"/> (C5). One file per instance (its key hashed to a filesystem-safe name). Loading a
/// missing or corrupt manifest returns an empty state (→ full ingest), so a broken file never blocks ingestion.
/// </summary>
public sealed class FileSyncStateStore : ISyncStateStore
{
    /// <summary>Default directory the per-instance manifests live in.</summary>
    public const string DefaultRoot = "data/sync-state";

    private readonly IFileSystem _fileSystem;
    private readonly string _root;

    /// <summary>Initializes a new <see cref="FileSyncStateStore"/>.</summary>
    /// <param name="fileSystem">Filesystem abstraction (the only permitted I/O path, Regel 2).</param>
    /// <param name="root">Directory the manifests are stored in; defaults to <see cref="DefaultRoot"/>.</param>
    public FileSyncStateStore(IFileSystem fileSystem, string? root = null)
    {
        _fileSystem = fileSystem;
        _root = string.IsNullOrWhiteSpace(root) ? DefaultRoot : root;
    }

    /// <inheritdoc />
    public async Task<IngestionSyncState> LoadAsync(string instanceKey, CancellationToken cancellationToken = default)
    {
        var path = PathFor(instanceKey);
        if (!_fileSystem.FileExists(path))
            return new IngestionSyncState();
        try
        {
            var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return new IngestionSyncState
            {
                ItemHashes = map is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(map, StringComparer.Ordinal),
            };
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            return new IngestionSyncState();   // corrupt/unreadable → treat as unknown → full ingest
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string instanceKey, IngestionSyncState state, CancellationToken cancellationToken = default)
    {
        _fileSystem.EnsureDirectoryExists(_root);
        var json = JsonSerializer.Serialize(state.ItemHashes);
        await _fileSystem.WriteAllTextAsync(PathFor(instanceKey), json, cancellationToken).ConfigureAwait(false);
    }

    private string PathFor(string instanceKey)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instanceKey))).ToLowerInvariant();
        return _fileSystem.CombinePath(_root, name + ".json");
    }
}
