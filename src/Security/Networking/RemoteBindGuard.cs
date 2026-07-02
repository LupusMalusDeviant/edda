using System.Net;

namespace Edda.Security.Networking;

/// <summary>
/// Startup guard that detects when the service would be reachable from a non-loopback (remote)
/// network interface without an authentication token configured.
/// <para>
/// Binding to a remote interface (for example <c>0.0.0.0</c>) while <c>EDDA_AUTH_TOKEN</c> is empty
/// exposes the API and UI to the network without authentication. The host calls
/// <see cref="IsInsecureRemoteBind(string?, string?, bool)"/> at startup to fail fast in that
/// situation, unless the operator has explicitly opted in via an override.
/// </para>
/// </summary>
public static class RemoteBindGuard
{
    /// <summary>
    /// Determines whether the given bind specification would expose the service on a non-loopback
    /// interface without authentication.
    /// </summary>
    /// <param name="bind">
    /// The configured bind specification. May be a bare host (<c>0.0.0.0</c>, <c>127.0.0.1</c>,
    /// <c>::1</c>), a host with port (<c>0.0.0.0:8080</c>), one or more URLs
    /// (<c>http://0.0.0.0:8080</c>, semicolon- or comma-separated), or a Kestrel wildcard
    /// (<c>+</c>/<c>*</c>). A <see langword="null"/> or empty value is treated as the safe loopback
    /// default.
    /// </param>
    /// <param name="token">
    /// The configured authentication token; any non-empty value makes every bind safe.
    /// </param>
    /// <param name="allowInsecure">
    /// When <see langword="true"/>, the operator has explicitly accepted an unauthenticated remote
    /// bind and the method always returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the bind targets at least one non-loopback host, no token is set,
    /// and the insecure override is not enabled; otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsInsecureRemoteBind(string? bind, string? token, bool allowInsecure)
    {
        if (allowInsecure)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return BindsToNonLoopback(bind);
    }

    /// <summary>
    /// Returns true if any host in the bind specification resolves to a non-loopback interface.
    /// </summary>
    private static bool BindsToNonLoopback(string? bind)
    {
        if (string.IsNullOrWhiteSpace(bind))
        {
            return false;
        }

        var entries = bind.Split(
            new[] { ';', ',' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            if (IsNonLoopbackHost(ExtractHost(entry)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the host portion from a single bind entry (bare host, host:port, or URL).
    /// </summary>
    private static string ExtractHost(string entry)
    {
        var value = entry;

        // Drop an optional scheme, e.g. "http://host:port/path" -> "host:port/path".
        var schemeIndex = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            value = value[(schemeIndex + 3)..];
        }

        // Drop an optional path, e.g. "host:port/path" -> "host:port".
        var slashIndex = value.IndexOf('/');
        if (slashIndex >= 0)
        {
            value = value[..slashIndex];
        }

        // Bracketed IPv6, e.g. "[::1]" or "[::1]:8080".
        if (value.StartsWith('['))
        {
            var closeIndex = value.IndexOf(']');
            return closeIndex > 0 ? value[1..closeIndex] : value.Trim('[', ']');
        }

        // "host:port" has exactly one colon; a bare IPv6 address has several and no port.
        var firstColon = value.IndexOf(':');
        if (firstColon >= 0 && value.IndexOf(':', firstColon + 1) < 0)
        {
            return value[..firstColon];
        }

        return value;
    }

    /// <summary>
    /// Returns true if the host is a wildcard/all-interfaces bind or a non-loopback address or name.
    /// </summary>
    private static bool IsNonLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // "+"/"*" are Kestrel wildcards meaning "all interfaces".
        if (host is "+" or "*")
        {
            return true;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            // 0.0.0.0 / :: (IPAddress.Any / IPv6Any) are non-loopback wildcard binds.
            return !IPAddress.IsLoopback(address);
        }

        // A host name other than "localhost" implies a reachable, non-loopback interface.
        return true;
    }
}
