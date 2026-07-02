using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Security.Mcp;

/// <summary>
/// File-backed <see cref="IMcpTokenStore"/> persisting to <c>&lt;data&gt;/mcp-tokens.json</c> via
/// <see cref="IFileSystem"/>. Only a salted SHA-256 hash of each token is stored (alongside the non-secret
/// scopes/metadata); the plaintext token is returned once at creation and is never recoverable. Tokens are
/// high-entropy random values, so a salted SHA-256 — not a slow password KDF — is sufficient; the per-token
/// salt additionally stops identical tokens from being cross-identifiable and defeats offline precomputation.
/// <para>
/// The file is a versioned envelope (<c>{ Version, Tokens }</c>). Legacy files written before salting were a
/// bare array of unsalted entries; they still load and resolve (entries without a salt are hashed the old,
/// unsalted way) and are upgraded to the versioned, salted-capable format on the next write.
/// </para>
/// </summary>
public sealed class FileMcpTokenStore : IMcpTokenStore
{
    private const string FileName = "mcp-tokens.json";
    private const string TokenPrefix = "mcp_";

    /// <summary>On-disk format version. Version 1 was an implicit, unversioned bare array of unsalted entries.</summary>
    private const int CurrentFormatVersion = 2;

    /// <summary>Per-token salt length in bytes (128-bit).</summary>
    private const int SaltSizeBytes = 16;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly string _directory;
    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="FileMcpTokenStore"/> class.</summary>
    /// <param name="fileSystem">Filesystem abstraction (only permitted file-I/O path).</param>
    /// <param name="timeProvider">Time provider for creation timestamps.</param>
    /// <param name="dataDirectory">Directory for the token file; defaults to <c>data</c>.</param>
    public FileMcpTokenStore(IFileSystem fileSystem, TimeProvider timeProvider, string? dataDirectory = null)
    {
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _directory = _fileSystem.GetFullPath(string.IsNullOrWhiteSpace(dataDirectory) ? "data" : dataDirectory);
        _path = _fileSystem.CombinePath(_directory, FileName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpTokenInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return entries.Select(e => e.ToInfo()).OrderBy(i => i.CreatedAt).ToList();
    }

    /// <inheritdoc />
    public async Task<McpTokenCreated> CreateAsync(
        string label,
        IReadOnlyList<string> tools,
        bool allowWrite,
        CancellationToken cancellationToken = default)
    {
        var token = TokenPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var entry = new Entry
        {
            Id = Base64Url(RandomNumberGenerator.GetBytes(6)),
            Label = string.IsNullOrWhiteSpace(label) ? "MCP-Token" : label.Trim(),
            Salt = Base64Url(salt),
            Hash = HashSalted(salt, token),
            Tools = tools.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal).ToList(),
            AllowWrite = allowWrite,
            CreatedAt = _timeProvider.GetUtcNow(),
        };

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            entries.Add(entry);
            await SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }

        return new McpTokenCreated { Token = token, Info = entry.ToInfo() };
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var removed = entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (removed > 0)
                await SaveAsync(entries, cancellationToken).ConfigureAwait(false);
            return removed > 0;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<McpTokenScopes?> ResolveAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);
        // Each entry may carry its own salt, so the expected hash is computed per entry (a salted entry uses
        // SHA-256(salt ‖ token); a legacy salt-less entry uses the old unsalted SHA-256(token)).
        var match = entries.FirstOrDefault(
            e => string.Equals(e.Hash, ExpectedHash(e, token), StringComparison.Ordinal));
        return match is null
            ? null
            : new McpTokenScopes { Id = match.Id, Tools = match.Tools, AllowWrite = match.AllowWrite };
    }

    private async Task<IReadOnlyList<Entry>> LoadAsync(CancellationToken ct)
    {
        if (!_fileSystem.FileExists(_path))
            return [];

        var json = await _fileSystem.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                // Legacy format (version 1): a bare array of entries, none of which carry a salt.
                JsonValueKind.Array => JsonSerializer.Deserialize<List<Entry>>(json) ?? [],
                // Current format (version 2+): a versioned envelope { Version, Tokens }.
                JsonValueKind.Object => JsonSerializer.Deserialize<TokenFile>(json)?.Tokens ?? [],
                _ => [],
            };
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveAsync(IReadOnlyList<Entry> entries, CancellationToken ct)
    {
        _fileSystem.EnsureDirectoryExists(_directory);
        // Always persist the versioned envelope: this upgrades a legacy bare-array file to the current
        // format on the first write, while leaving individual salt-less entries intact (still accepted).
        var file = new TokenFile { Version = CurrentFormatVersion, Tokens = entries.ToList() };
        var json = JsonSerializer.Serialize(file, SerializerOptions);
        await _fileSystem.WriteAllTextAsync(_path, json, ct).ConfigureAwait(false);
    }

    /// <summary>Computes the expected stored hash for <paramref name="entry"/> given a candidate token.</summary>
    private static string ExpectedHash(Entry entry, string token)
        => string.IsNullOrEmpty(entry.Salt)
            ? HashUnsalted(token)
            : HashSalted(FromBase64Url(entry.Salt), token);

    /// <summary>Salted hash: SHA-256(salt ‖ token), hex-encoded.</summary>
    private static string HashSalted(byte[] salt, string token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var buffer = new byte[salt.Length + tokenBytes.Length];
        Buffer.BlockCopy(salt, 0, buffer, 0, salt.Length);
        Buffer.BlockCopy(tokenBytes, 0, buffer, salt.Length, tokenBytes.Length);
        return Convert.ToHexString(SHA256.HashData(buffer));
    }

    /// <summary>Legacy unsalted hash: SHA-256(token), hex-encoded. Kept to verify pre-salt entries.</summary>
    private static string HashUnsalted(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    /// <summary>Versioned on-disk envelope. Legacy files were a bare <see cref="Entry"/> array (version 1).</summary>
    private sealed class TokenFile
    {
        public int Version { get; set; } = CurrentFormatVersion;
        public List<Entry> Tokens { get; set; } = [];
    }

    private sealed class Entry
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// <summary>Base64url per-token salt; <see langword="null"/>/empty for legacy entries hashed without a salt.</summary>
        public string? Salt { get; set; }

        public string Hash { get; set; } = string.Empty;
        public List<string> Tools { get; set; } = [];
        public bool AllowWrite { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public McpTokenInfo ToInfo() => new()
        {
            Id = Id,
            Label = Label,
            Tools = Tools,
            AllowWrite = AllowWrite,
            CreatedAt = CreatedAt,
        };
    }
}
