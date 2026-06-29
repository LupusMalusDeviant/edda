using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.Security.Audit;

/// <summary>
/// Verifies the integrity of the complete <c>audit.jsonl</c> Merkle chain.
/// Checks every entry for a valid HMAC signature, a monotonically increasing
/// sequence number, and an unbroken Merkle hash link to its predecessor.
/// Intended for offline or on-demand verification (e.g. via the admin REST endpoint).
/// </summary>
public sealed class MerkleAuditVerifier : IMerkleAuditVerifier
{
    private const string KeyFilePath = "data/.credential-key";

    private readonly IFileSystem                _fileSystem;
    private readonly ILogger<MerkleAuditVerifier> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="MerkleAuditVerifier"/>.
    /// </summary>
    /// <param name="fileSystem">Abstracted filesystem used to read the key file and the audit log.</param>
    /// <param name="logger">Logger for verification diagnostics.</param>
    public MerkleAuditVerifier(IFileSystem fileSystem, ILogger<MerkleAuditVerifier> logger)
    {
        _fileSystem = fileSystem;
        _logger     = logger;
    }

    /// <summary>
    /// Reads and verifies the entire audit log at <paramref name="logPath"/>.
    /// </summary>
    /// <param name="logPath">Path to the <c>audit.jsonl</c> file to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ChainVerificationResult"/> describing the total entry count, overall
    /// validity, and the list of any detected integrity violations.
    /// </returns>
    public async Task<ChainVerificationResult> VerifyAsync(string logPath, CancellationToken ct = default)
    {
        var hmacKey = await LoadHmacKeyAsync(ct).ConfigureAwait(false);

        if (!_fileSystem.FileExists(logPath))
        {
            _logger.LogInformation("Audit log not found at {LogPath} — chain is trivially valid", logPath);
            return new ChainVerificationResult(0, true, []);
        }

        var content = await _fileSystem.ReadAllTextAsync(logPath, ct).ConfigureAwait(false);
        var lines   = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
            return new ChainVerificationResult(0, true, []);

        var issues          = new List<ChainIssue>();
        string? expectedPrevHash = null;
        long    expectedSeq      = 1;

        for (var i = 0; i < lines.Length; i++)
        {
            SignedAuditEntry signed;
            AuditEntry       entry;

            try
            {
                signed = JsonSerializer.Deserialize<SignedAuditEntry>(lines[i])
                    ?? throw new JsonException("Deserialized value was null");
                entry = JsonSerializer.Deserialize<AuditEntry>(signed.Entry)
                    ?? throw new JsonException("Deserialized entry was null");
            }
            catch (Exception ex)
            {
                issues.Add(new ChainIssue(i + 1, "PARSE_ERROR",
                    $"Line {i + 1} could not be parsed: {ex.Message}"));
                expectedSeq++;
                continue;
            }

            // 1. HMAC integrity check
            if (!VerifyHmac(hmacKey, signed.Entry, signed.Mac))
                issues.Add(new ChainIssue(i + 1, "HMAC_INVALID",
                    $"Entry {entry.Seq}: HMAC mismatch — entry may have been tampered with"));

            // 2. Sequence number continuity check
            if (entry.Seq != expectedSeq)
                issues.Add(new ChainIssue(i + 1, "SEQ_GAP",
                    $"Expected seq {expectedSeq}, got {entry.Seq} — one or more entries may be missing or reordered"));

            // 3. Merkle chain link check (only from the second entry onward)
            if (expectedPrevHash is not null && entry.PrevHash != expectedPrevHash)
                issues.Add(new ChainIssue(i + 1, "CHAIN_BROKEN",
                    $"Entry {entry.Seq}: PrevHash mismatch — log may have been tampered or entries reordered"));

            expectedPrevHash = signed.EntryHash;
            expectedSeq      = entry.Seq + 1;
        }

        var isValid = issues.Count == 0;
        _logger.LogInformation(
            "Audit chain verification complete: {TotalEntries} entries, valid={IsValid}, issues={IssueCount}",
            lines.Length, isValid, issues.Count);

        return new ChainVerificationResult(lines.Length, isValid, issues);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the HMAC key from the credential key file.
    /// Returns an empty array if the key file does not exist, which will cause
    /// all HMAC checks to fail (returning <c>HMAC_INVALID</c> for every entry).
    /// </summary>
    private async Task<byte[]> LoadHmacKeyAsync(CancellationToken ct)
    {
        if (!_fileSystem.FileExists(KeyFilePath))
        {
            _logger.LogWarning(
                "HMAC key file not found at {KeyFilePath} — HMAC verification will fail for all entries",
                KeyFilePath);
            return [];
        }

        var rawKey = await _fileSystem.ReadAllBytesAsync(KeyFilePath, ct).ConfigureAwait(false);
        return SHA256.HashData(rawKey);
    }

    /// <summary>
    /// Verifies that the HMAC-SHA256 over <paramref name="entryJson"/> using
    /// <paramref name="hmacKey"/> matches the stored <paramref name="mac"/>.
    /// Uses constant-time comparison to prevent timing attacks.
    /// Returns <c>false</c> if <paramref name="hmacKey"/> is empty.
    /// </summary>
    /// <param name="hmacKey">The 32-byte HMAC key.</param>
    /// <param name="entryJson">The serialized entry JSON string to verify.</param>
    /// <param name="mac">The expected Base64-encoded HMAC-SHA256 MAC.</param>
    /// <returns><c>true</c> if the MAC is valid; otherwise <c>false</c>.</returns>
    private static bool VerifyHmac(byte[] hmacKey, string entryJson, string mac)
    {
        if (hmacKey.Length == 0) return false;

        var data     = Encoding.UTF8.GetBytes(entryJson);
        var expected = Convert.ToBase64String(HMACSHA256.HashData(hmacKey, data));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(mac));
    }
}
