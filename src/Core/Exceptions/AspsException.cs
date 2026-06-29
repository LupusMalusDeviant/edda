namespace Edda.Core.Exceptions;

/// <summary>
/// Base exception for all ASPS.ai API errors.
/// Carries the HTTP status code and response body for diagnostics.
/// </summary>
public class AspsApiException : EddaException
{
    /// <summary>HTTP status code returned by the ASPS.ai API.</summary>
    public int StatusCode { get; }

    /// <summary>Raw response body for debugging purposes.</summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Initializes a new AspsApiException.
    /// </summary>
    /// <param name="message">Human-readable error description.</param>
    /// <param name="statusCode">HTTP status code from the ASPS.ai response.</param>
    /// <param name="responseBody">Raw response body for diagnostics.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public AspsApiException(
        string message,
        int statusCode,
        string? responseBody = null,
        Exception? innerException = null)
        : base("AspsClient", message, innerException: innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Thrown when the ASPS.ai API returns 401, indicating an invalid or expired token.
/// </summary>
public sealed class AspsAuthenticationException : AspsApiException
{
    /// <summary>Initializes a new AspsAuthenticationException.</summary>
    public AspsAuthenticationException()
        : base("ASPS.ai API rejected the request due to invalid or expired token (401).",
               statusCode: 401) { }
}

/// <summary>
/// Thrown when the requested ASPS.ai project is not found (404).
/// </summary>
public sealed class AspsProjectNotFoundException : AspsApiException
{
    /// <summary>The slug of the project that was not found.</summary>
    public string Slug { get; }

    /// <summary>Initializes a new AspsProjectNotFoundException.</summary>
    /// <param name="slug">The project slug that was not found.</param>
    public AspsProjectNotFoundException(string slug)
        : base($"ASPS.ai project with slug '{slug}' was not found (404).",
               statusCode: 404)
    {
        Slug = slug;
    }
}
