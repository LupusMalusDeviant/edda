namespace Edda.Core.Exceptions;

/// <summary>
/// Base class for all domain exceptions in the Edda.
/// Provides a Component identifier and optional CorrelationId for structured logging.
/// All custom exceptions in this system inherit from this class.
/// </summary>
public abstract class EddaException : Exception
{
    /// <summary>
    /// The subsystem that raised this exception (e.g. "AKG", "Provider", "Sandbox").
    /// Used as a structured log property.
    /// </summary>
    public string Component { get; }

    /// <summary>
    /// Distributed tracing correlation ID for cross-service request tracking.
    /// May be null for locally originated exceptions.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Initializes a new EddaException.
    /// </summary>
    /// <param name="component">The subsystem where the exception originated.</param>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    protected EddaException(
        string component,
        string message,
        string? correlationId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Component = component;
        CorrelationId = correlationId;
    }
}
