using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Security.Mcp;

/// <summary>
/// File-backed <see cref="IMcpTokenStore"/> persisting to <c>&lt;data&gt;/mcp-tokens.json</c> via
/// <see cref="IFileSystem"/>. Only a SHA-256 hash of each token is stored (alongside the non-secret
/// scopes/metadata); the plaintext token is returned once at creation and is never recoverable. Tokens are
/// high-entropy random values, so a plain SHA-256 — not a slow password KDF — is sufficient.
/// </summary>
public sealed class FileMcpTokenStore : IMcpTokenStore
{
    private const string FileName = "mcp-tokens.json";
    private const string TokenPrefix = "mcp_";

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
        var entry = new Entry
        {
            Id = Base64Url(RandomNumberGenerator.GetBytes(6)),
            Label = string.IsNullOrWhiteSpace(label) ? "MCP-Token" : label.Trim(),
            Hash = Hash(token),
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

        var hash = Hash(token);
        var entries = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var match = entries.FirstOrDefault(e => string.Equals(e.Hash, hash, StringComparison.Ordinal));
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
            return JsonSerializer.Deserialize<List<Entry>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task SaveAsync(IReadOnlyList<Entry> entries, CancellationToken ct)
    {
        _fileSystem.EnsureDirectoryExists(_directory);
        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        await _fileSystem.WriteAllTextAsync(_path, json, ct).ConfigureAwait(false);
    }

    private static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class Entry
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
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
