namespace Edda.Hosting.Authentication;

/// <summary>
/// Extracts the bearer auth token from a request's inputs and reports whether it came from the deprecated
/// <c>?token=</c> query parameter (issue A6). The <c>Authorization: Bearer</c> header takes precedence; a query
/// token is still accepted for backward compatibility but flagged, so the caller can log a deprecation warning —
/// query strings leak into logs, browser history and <c>Referer</c> headers. Pure and side-effect-free.
/// </summary>
internal static class AuthTokenExtractor
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Extracts the token and its source from the Authorization header and the query token value.</summary>
    /// <param name="authorizationHeader">The raw <c>Authorization</c> header value (may be null or empty).</param>
    /// <param name="queryToken">The raw <c>?token=</c> query value (null when absent).</param>
    /// <returns>The token (null if none was supplied) and whether it was taken from the query parameter.</returns>
    public static (string? Token, bool FromQuery) Extract(string? authorizationHeader, string? queryToken)
    {
        if (!string.IsNullOrEmpty(authorizationHeader)
            && authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return (authorizationHeader[BearerPrefix.Length..].Trim(), false);

        return string.IsNullOrEmpty(queryToken) ? (null, false) : (queryToken, true);
    }
}
