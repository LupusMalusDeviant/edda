namespace Edda.Core.Exceptions;

/// <summary>
/// Thrown when the agent runtime encounters an unrecoverable error during pipeline execution.
/// The Phase property identifies which of the 10 processing phases caused the failure.
/// </summary>
public class AgentException : EddaException
{
    /// <summary>The pipeline phase (0–10) in which the error occurred.</summary>
    public int Phase { get; }

    /// <summary>
    /// Initializes a new AgentException.
    /// </summary>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="phase">Pipeline phase index (0–10) where the error occurred.</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public AgentException(string message, int phase, string? correlationId = null,
        Exception? innerException = null)
        : base("Agent", message, correlationId, innerException)
    {
        Phase = phase;
    }
}
