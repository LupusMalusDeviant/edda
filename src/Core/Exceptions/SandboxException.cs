namespace Edda.Core.Exceptions;

/// <summary>
/// Base exception for sandbox infrastructure failures.
/// Distinct from a validator reporting a rule violation — this represents a runtime failure.
/// </summary>
public class SandboxException : EddaException
{
    /// <summary>The sandbox type that failed (e.g. "docker", "wasm").</summary>
    public string SandboxType { get; }

    /// <summary>
    /// Initializes a new SandboxException.
    /// </summary>
    /// <param name="sandboxType">The sandbox type (e.g. "docker" or "wasm").</param>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public SandboxException(string sandboxType, string message, Exception? innerException = null)
        : base("Sandbox", message, innerException: innerException)
    {
        SandboxType = sandboxType;
    }
}

/// <summary>
/// Thrown when a sandbox container or runtime cannot be started.
/// </summary>
public sealed class SandboxStartException : SandboxException
{
    /// <summary>Initializes a new SandboxStartException.</summary>
    public SandboxStartException(string sandboxType, Exception? innerException = null)
        : base(sandboxType,
               $"Failed to start sandbox of type '{sandboxType}'.",
               innerException) { }
}

/// <summary>
/// Thrown when a TDK validator script exceeds the 10-second execution timeout.
/// </summary>
public sealed class SandboxTimeoutException : SandboxException
{
    /// <summary>The timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Initializes a new SandboxTimeoutException.</summary>
    public SandboxTimeoutException(string sandboxType, TimeSpan timeout)
        : base(sandboxType,
               $"Sandbox '{sandboxType}' script timed out after {timeout.TotalSeconds}s.")
    {
        Timeout = timeout;
    }
}
