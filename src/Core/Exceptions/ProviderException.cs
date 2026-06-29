namespace Edda.Core.Exceptions;

/// <summary>
/// Base exception for LLM and embedding provider errors.
/// Used by ResilientModelClient to trigger retry or circuit-breaker logic.
/// Subclasses distinguish 401, 429, and 5xx scenarios for targeted resilience handling.
/// </summary>
public class ProviderException : EddaException
{
    /// <summary>Name of the provider that returned the error (e.g. "Anthropic", "OpenAI").</summary>
    public string Provider { get; }

    /// <summary>HTTP status code returned by the provider, if available.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// Initializes a new ProviderException.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="statusCode">HTTP status code from the provider response, if applicable.</param>
    /// <param name="correlationId">Optional correlation ID for distributed tracing.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public ProviderException(
        string provider,
        string message,
        int? statusCode = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base("Provider", message, correlationId, innerException)
    {
        Provider = provider;
        StatusCode = statusCode;
    }
}

/// <summary>
/// Thrown when the provider returns a 401 or 403 response, indicating an invalid API key.
/// Circuit breaker should open immediately — retrying will not help.
/// </summary>
public sealed class ProviderAuthException : ProviderException
{
    /// <summary>Initializes a new ProviderAuthException.</summary>
    public ProviderAuthException(string provider)
        : base(provider,
               $"Provider '{provider}' rejected the request due to invalid credentials (401/403).",
               statusCode: 401) { }
}

/// <summary>
/// Thrown when the provider returns a 429 Too Many Requests response.
/// Triggers exponential backoff in ResilientModelClient.
/// </summary>
public sealed class ProviderRateLimitException : ProviderException
{
    /// <summary>Retry-After duration suggested by the provider, if available.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Initializes a new ProviderRateLimitException.</summary>
    public ProviderRateLimitException(string provider, TimeSpan? retryAfter = null)
        : base(provider,
               $"Provider '{provider}' rate limit exceeded (429).",
               statusCode: 429)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Thrown when the provider returns a 5xx error or is unreachable.
/// Triggers retry with exponential backoff.
/// </summary>
public sealed class ProviderUnavailableException : ProviderException
{
    /// <summary>Initializes a new ProviderUnavailableException.</summary>
    public ProviderUnavailableException(string provider, int? statusCode = null,
        Exception? innerException = null)
        : base(provider,
               $"Provider '{provider}' is temporarily unavailable.",
               statusCode,
               innerException: innerException) { }
}
