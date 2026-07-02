namespace Edda.Security.Authentication;

/// <summary>
/// Parses a bearer token from an HTTP <c>Authorization</c> header value. Used instead of accepting a
/// token via a query parameter, which would leak into server logs, browser history, and referrers.
/// </summary>
public static class BearerTokenParser
{
    private const string Prefix = "Bearer ";

    /// <summary>
    /// Extracts the token from an <c>Authorization: Bearer &lt;token&gt;</c> header value.
    /// </summary>
    /// <param name="authorizationHeaderValue">The raw header value (may be <see langword="null"/> or empty).</param>
    /// <returns>
    /// The trimmed token when the value starts with the case-insensitive <c>Bearer </c> scheme and a
    /// non-empty token follows; otherwise <see langword="null"/>.
    /// </returns>
    public static string? Parse(string? authorizationHeaderValue)
    {
        if (string.IsNullOrEmpty(authorizationHeaderValue)
            || !authorizationHeaderValue.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeaderValue[Prefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }
}
