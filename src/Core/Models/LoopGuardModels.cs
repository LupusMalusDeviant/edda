namespace Edda.Core.Models;

/// <summary>
/// Graduated response to a detected loop condition in the ToolLoop.
/// ToolLoop maps each decision to a concrete action (continue, warn, stop, or abort).
/// </summary>
public enum LoopGuardDecision
{
    /// <summary>No loop pattern detected. Continue the ToolLoop normally.</summary>
    Allow,

    /// <summary>
    /// Suspicious repetition detected. Inject a warning into the next tool-results
    /// message so the model is informed, but allow execution to continue for now.
    /// </summary>
    Warn,

    /// <summary>
    /// Clear loop detected. Stop the ToolLoop immediately and pass the already-collected
    /// tool results to the synthesis phase.
    /// </summary>
    Block,

    /// <summary>
    /// Emergency abort: the session-wide tool-call limit was exceeded, or a Block
    /// condition recurred within the same turn. Return an error response immediately
    /// without a synthesis pass.
    /// </summary>
    CircuitBreak
}

/// <summary>
/// Result of an <c>ILoopGuard</c> evaluation step, returned after each
/// tool invocation is recorded.
/// </summary>
/// <param name="Decision">What the ToolLoop should do next.</param>
/// <param name="Reason">Human-readable explanation used for structured logging.</param>
/// <param name="WarningText">
/// Non-null only when <see cref="Decision"/> is <see cref="LoopGuardDecision.Warn"/>.
/// Injected as a system note into the next tool-results message shown to the model.
/// </param>
public sealed record LoopGuardVerdict(
    LoopGuardDecision Decision,
    string Reason,
    string? WarningText = null);

/// <summary>
/// Immutable fingerprint of a single tool invocation (call + outcome).
/// Two invocations sharing the same fingerprint are considered identical repetitions
/// and counted toward the warn/block thresholds.
/// </summary>
/// <param name="ToolName">Name of the executed tool.</param>
/// <param name="CallHash">
/// First 16 hex characters of the SHA-256 hash of the serialised argument dictionary.
/// </param>
/// <param name="ResultHash">
/// First 16 hex characters of the SHA-256 hash of the result content (or error message).
/// Including the result makes the fingerprint outcome-aware: a polling tool that
/// eventually returns a different result is not treated as a loop.
/// </param>
public sealed record ToolInvocationFingerprint(
    string ToolName,
    string CallHash,
    string ResultHash);
