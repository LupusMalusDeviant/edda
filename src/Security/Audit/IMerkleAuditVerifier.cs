using Edda.Core.Models;

namespace Edda.Security.Audit;

/// <summary>
/// Verifies the integrity of the audit Merkle chain (HMAC signatures, sequence continuity, hash links).
/// </summary>
public interface IMerkleAuditVerifier
{
    /// <summary>Reads and verifies the entire audit log at the given path.</summary>
    /// <param name="logPath">Path to the <c>audit.jsonl</c> file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The verification result: entry count, overall validity, and detected issues.</returns>
    Task<ChainVerificationResult> VerifyAsync(string logPath, CancellationToken ct = default);
}
