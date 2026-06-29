namespace Edda.Core.Exceptions;

/// <summary>
/// Base exception for multi-agent clone lifecycle errors.
/// </summary>
public class CloneException : EddaException
{
    /// <summary>Initializes a new CloneException.</summary>
    public CloneException(string message, Exception? innerException = null)
        : base("Clone", message, innerException: innerException) { }
}

/// <summary>
/// Thrown when a new clone cannot be spawned because the maximum of 5 concurrent clones
/// is already active.
/// </summary>
public sealed class MaxClonesReachedException : CloneException
{
    /// <summary>The maximum number of concurrent clones allowed.</summary>
    public int MaxClones { get; }

    /// <summary>Initializes a new MaxClonesReachedException.</summary>
    public MaxClonesReachedException(int maxClones = 5)
        : base($"Maximum number of concurrent clones ({maxClones}) has been reached.")
    {
        MaxClones = maxClones;
    }
}

/// <summary>
/// Thrown when a clone container fails to become healthy within the startup timeout.
/// </summary>
public sealed class CloneStartupException : CloneException
{
    /// <summary>Identifier of the clone that failed to start.</summary>
    public string CloneId { get; }

    /// <summary>Initializes a new CloneStartupException.</summary>
    public CloneStartupException(string cloneId, TimeSpan timeout)
        : base($"Clone '{cloneId}' did not become healthy within {timeout.TotalSeconds}s.")
    {
        CloneId = cloneId;
    }
}
