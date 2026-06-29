using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Append-only, HMAC-signed audit log.
/// Every entry is written to data/audit.jsonl and cannot be modified after writing.
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// Writes an audit log entry. Thread-safe.
    /// </summary>
    /// <param name="eventType">The type of event being logged.</param>
    /// <param name="userId">The user associated with the event.</param>
    /// <param name="description">Human-readable description of the event.</param>
    /// <param name="metadata">Optional key-value pairs with additional event context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(
        AuditEvent eventType,
        string userId,
        string description,
        IDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default);
}
