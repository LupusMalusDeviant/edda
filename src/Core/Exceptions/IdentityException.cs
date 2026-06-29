namespace Edda.Core.Exceptions;

/// <summary>
/// Base exception for authentication and identity errors.
/// </summary>
public class IdentityException : EddaException
{
    /// <summary>Initializes a new IdentityException.</summary>
    public IdentityException(string message, Exception? innerException = null)
        : base("Identity", message, innerException: innerException) { }
}

/// <summary>
/// Thrown when a user attempts to perform an action they are not authorized for.
/// Results in an HTTP 403 response at the Gateway layer.
/// </summary>
public sealed class UnauthorizedException : IdentityException
{
    /// <summary>The user ID that was denied access.</summary>
    public string? UserId { get; }

    /// <summary>Initializes a new UnauthorizedException.</summary>
    /// <param name="userId">The user who was denied.</param>
    /// <param name="action">Description of the action that was denied.</param>
    public UnauthorizedException(string? userId, string action)
        : base($"User '{userId ?? "anonymous"}' is not authorized to: {action}")
    {
        UserId = userId;
    }
}
