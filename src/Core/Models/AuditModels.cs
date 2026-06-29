namespace Edda.Core.Models;

/// <summary>
/// A single entry in the HMAC+Merkle-chain audit log.
/// </summary>
public sealed record AuditEntry
{
    /// <summary>Monotonically increasing sequence number. Starts at 1.</summary>
    public required long Seq { get; init; }

    /// <summary>UTC timestamp of the event.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Event classification (string representation of <see cref="AuditEvent"/>).</summary>
    public required string EventType { get; init; }

    /// <summary>User who triggered the event.</summary>
    public required string UserId { get; init; }

    /// <summary>Human-readable description of the event.</summary>
    public required string Description { get; init; }

    /// <summary>Additional structured metadata (sanitized — must not contain secrets).</summary>
    public IDictionary<string, object?>? Metadata { get; init; }

    /// <summary>
    /// SHA-256 hash of the previous entry's serialized JSON, forming the Merkle chain.
    /// Null only for the very first entry (genesis block).
    /// </summary>
    public string? PrevHash { get; init; }
}

/// <summary>
/// The signed and hashed representation of a single <see cref="AuditEntry"/> stored in <c>audit.jsonl</c>.
/// Each line in the audit file is the JSON serialization of one <see cref="SignedAuditEntry"/>.
/// </summary>
/// <param name="Entry">JSON-serialized <see cref="AuditEntry"/>.</param>
/// <param name="Mac">HMAC-SHA256 (Base64-encoded) computed over <see cref="Entry"/>.</param>
/// <param name="EntryHash">SHA-256 hex digest of <see cref="Entry"/>, used as <see cref="AuditEntry.PrevHash"/> by the next entry.</param>
public sealed record SignedAuditEntry(string Entry, string Mac, string EntryHash);

/// <summary>
/// Summarizes the result of a full Merkle audit chain verification.
/// </summary>
/// <param name="TotalEntries">Total number of lines examined in the audit file.</param>
/// <param name="IsValid"><c>true</c> if every entry passed both the HMAC check and the chain-continuity check.</param>
/// <param name="Issues">List of detected integrity violations. Empty when <see cref="IsValid"/> is <c>true</c>.</param>
public sealed record ChainVerificationResult(
    int TotalEntries,
    bool IsValid,
    IReadOnlyList<ChainIssue> Issues);

/// <summary>
/// Describes a single integrity violation detected during audit chain verification.
/// </summary>
/// <param name="LineNumber">1-based line number in the audit file where the issue was found.</param>
/// <param name="IssueType">
/// Short machine-readable code identifying the violation type.
/// Known values: <c>HMAC_INVALID</c>, <c>SEQ_GAP</c>, <c>CHAIN_BROKEN</c>, <c>PARSE_ERROR</c>.
/// </param>
/// <param name="Description">Human-readable description of the specific violation.</param>
public sealed record ChainIssue(int LineNumber, string IssueType, string Description);
