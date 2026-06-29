using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Security.Audit;

/// <summary>
/// Append-only HMAC-signed audit log with Merkle-chain linking.
/// Each entry contains the SHA-256 hash of the previous entry, forming a
/// tamper-evident chain: any deletion or reordering breaks the chain.
/// Every entry is also individually HMAC-SHA256-signed to detect in-place tampering.
/// The HMAC key is derived from a 32-byte key file stored at <c>data/.credential-key</c>.
/// Log entries are written to daily-rotated files inside the <c>data/audit.jsonl/</c>
/// directory, e.g. <c>data/audit.jsonl/audit-2026-03-05.jsonl</c>.
/// The Merkle chain is continuous across day boundaries: chain state is restored from
/// the most recent daily file on startup.
/// All write operations are serialized via a <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class HmacAuditLog : IAuditLog
{
    /// <summary>Directory that contains the daily-rotated audit files.</summary>
    private const string AuditDir    = "data/audit.jsonl";
    private const string KeyFilePath = "data/.credential-key";

    private readonly IFileSystem           _fileSystem;
    private readonly TimeProvider          _timeProvider;
    private readonly ILogger<HmacAuditLog> _logger;
    private readonly SemaphoreSlim         _writeLock = new(1, 1);

    // In-memory chain state — only ever accessed inside _writeLock.
    private byte[]?  _hmacKey;
    private bool     _chainInitialized;
    private long     _lastSeq;
    private string?  _lastEntryHash;

    /// <summary>
    /// Initializes a new instance of <see cref="HmacAuditLog"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracted filesystem for all I/O.</param>
    /// <param name="timeProvider">Time provider for deterministic timestamps.</param>
    /// <param name="logger">Logger for operational diagnostics.</param>
    public HmacAuditLog(
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        ILogger<HmacAuditLog> logger)
    {
        _fileSystem   = fileSystem;
        _timeProvider = timeProvider;
        _logger       = logger;
    }

    /// <inheritdoc />
    public async Task LogAsync(
        AuditEvent eventType,
        string userId,
        string description,
        IDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Lazy initialization: load the HMAC key and restore chain state on the very
            // first write. Both are done inside the lock so initialization is guaranteed
            // to occur exactly once, even under concurrent callers.
            if (!_chainInitialized)
            {
                _hmacKey = await LoadHmacKeyAsync(cancellationToken).ConfigureAwait(false);
                await InitializeChainStateAsync(cancellationToken).ConfigureAwait(false);
                _chainInitialized = true;
            }

            var seq   = ++_lastSeq;
            var entry = new AuditEntry
            {
                Seq         = seq,
                Timestamp   = _timeProvider.GetUtcNow(),
                EventType   = eventType.ToString(),
                UserId      = userId,
                Description = description,
                Metadata    = metadata,
                PrevHash    = _lastEntryHash,   // Merkle link to the previous entry
            };

            var entryJson  = JsonSerializer.Serialize(entry);
            var hmacKey    = _hmacKey!; // non-null: set by LoadHmacKeyAsync above
            var mac        = ComputeHmac(hmacKey, entryJson);
            var entryHash  = ComputeSha256(entryJson);
            _lastEntryHash = entryHash;

            var signedEntry = new SignedAuditEntry(entryJson, mac, entryHash);
            var line        = JsonSerializer.Serialize(signedEntry) + "\n";

            _fileSystem.EnsureDirectoryExists(AuditDir);
            var datedPath = GetDatedFilePath();
            await _fileSystem.AppendAllTextAsync(datedPath, line, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Audit entry written: seq={Seq} event={EventType} user={UserId}",
                seq, eventType, userId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Verifies the HMAC signature of a single signed audit entry.
    /// Uses constant-time comparison to prevent timing attacks.
    /// Returns <c>false</c> if the HMAC key has not yet been loaded (i.e., before
    /// the first <see cref="LogAsync"/> call on this instance).
    /// </summary>
    /// <param name="signedEntry">The signed entry to verify.</param>
    /// <returns><c>true</c> if the HMAC is valid; otherwise <c>false</c>.</returns>
    public bool VerifyEntry(SignedAuditEntry signedEntry)
    {
        var key = _hmacKey;
        if (key is null) return false;

        try
        {
            var expected = ComputeHmac(key, signedEntry.Entry);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signedEntry.Mac));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HMAC verification failed");
            return false;
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the path of the audit file for the current UTC date,
    /// located inside <see cref="AuditDir"/>.
    /// Example: <c>data/audit.jsonl/audit-2026-03-05.jsonl</c>.
    /// </summary>
    private string GetDatedFilePath() =>
        _fileSystem.CombinePath(AuditDir, $"audit-{_timeProvider.GetUtcNow():yyyy-MM-dd}.jsonl");

    /// <summary>
    /// Lazily loads or generates the HMAC key.
    /// If the key file does not exist, 32 random bytes are generated and persisted.
    /// The raw file bytes are hashed with SHA-256 to produce the 32-byte HMAC key.
    /// </summary>
    private async Task<byte[]> LoadHmacKeyAsync(CancellationToken ct)
    {
        byte[] rawKey;
        if (_fileSystem.FileExists(KeyFilePath))
        {
            rawKey = await _fileSystem.ReadAllBytesAsync(KeyFilePath, ct).ConfigureAwait(false);
        }
        else
        {
            rawKey = RandomNumberGenerator.GetBytes(32);
            _fileSystem.EnsureDirectoryExists("data");
            await _fileSystem.WriteAllBytesAsync(KeyFilePath, rawKey, ct).ConfigureAwait(false);
        }

        return SHA256.HashData(rawKey);
    }

    /// <summary>
    /// Restores <see cref="_lastSeq"/> and <see cref="_lastEntryHash"/> for Merkle-chain
    /// continuity after a process restart by reading the last line of the most recent
    /// daily audit file inside <see cref="AuditDir"/>.
    /// No-op if the audit directory does not exist or contains no files.
    /// Must only be called while holding <see cref="_writeLock"/>.
    /// </summary>
    private async Task InitializeChainStateAsync(CancellationToken ct)
    {
        if (!_fileSystem.DirectoryExists(AuditDir)) return;

        // Daily files are named audit-YYYY-MM-DD.jsonl; descending lexicographic order
        // equals descending date order, so the first element is the most recent file.
        var files = _fileSystem.EnumerateFiles(AuditDir, "audit-*.jsonl")
            .OrderByDescending(f => f)
            .ToList();

        if (files.Count == 0) return;

        var lastFilePath = files[0];
        var content      = await _fileSystem.ReadAllTextAsync(lastFilePath, ct).ConfigureAwait(false);
        var lines        = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return;

        try
        {
            var lastSigned = JsonSerializer.Deserialize<SignedAuditEntry>(lines[^1]);
            // Entry is null when the file was produced by an older, non-HMAC audit format.
            // In that case skip chain restoration and start a fresh chain from seq=1.
            if (lastSigned?.Entry is null) return;

            var lastEntry = JsonSerializer.Deserialize<AuditEntry>(lastSigned.Entry);
            if (lastEntry is null) return;

            _lastSeq       = lastEntry.Seq;
            _lastEntryHash = lastSigned.EntryHash;

            _logger.LogDebug(
                "Audit chain restored from {FilePath}: seq={Seq}",
                lastFilePath, _lastSeq);
        }
        catch (Exception ex)
        {
            // Legacy or malformed last entry — start a fresh chain rather than crashing.
            _logger.LogWarning(
                "Audit chain state could not be restored from '{FilePath}' — starting a new chain. Details: {Error}",
                lastFilePath, ex.Message);
        }
    }

    /// <summary>
    /// Computes an HMAC-SHA256 over the UTF-8 encoding of <paramref name="data"/>
    /// using the provided <paramref name="key"/>, and returns the result as a Base64 string.
    /// </summary>
    /// <param name="key">The 32-byte HMAC key.</param>
    /// <param name="data">The string to authenticate.</param>
    /// <returns>Base64-encoded HMAC-SHA256 digest.</returns>
    private static string ComputeHmac(byte[] key, string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        return Convert.ToBase64String(HMACSHA256.HashData(key, bytes));
    }

    /// <summary>
    /// Computes the SHA-256 hash of the UTF-8 encoding of <paramref name="data"/>
    /// and returns the result as a lowercase hexadecimal string.
    /// Used to form the Merkle chain link stored in <see cref="SignedAuditEntry.EntryHash"/>.
    /// </summary>
    /// <param name="data">The string to hash.</param>
    /// <returns>64-character lowercase hex string representing the SHA-256 digest.</returns>
    private static string ComputeSha256(string data)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
