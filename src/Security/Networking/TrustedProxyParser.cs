using System.Net;

namespace Edda.Security.Networking;

/// <summary>
/// Parses the operator-configured list of trusted reverse-proxy addresses (<c>EDDA_TRUSTED_PROXIES</c>)
/// into <see cref="IPAddress"/> values for ASP.NET Core's forwarded-headers handling.
/// <para>
/// Behind a reverse proxy the direct peer is the proxy, so <c>X-Forwarded-For</c>/<c>-Proto</c> must be
/// honored to recover the real client IP and scheme — but only from proxies the operator explicitly
/// trusts, otherwise a client could spoof its source IP. An empty or whitespace configuration yields an
/// empty list, and the host then leaves forwarded headers ignored (the safe default).
/// </para>
/// </summary>
public static class TrustedProxyParser
{
    /// <summary>
    /// Parses a comma- or semicolon-separated list of proxy IP addresses. Blank entries are skipped and
    /// entries that are not valid IPv4/IPv6 literals are ignored (fail-safe: an unparseable proxy is simply
    /// not trusted). Duplicate addresses are collapsed, preserving first-seen order.
    /// </summary>
    /// <param name="trustedProxies">
    /// The raw configuration value, e.g. <c>"10.0.0.1, 10.0.0.2"</c>. <see langword="null"/> or whitespace
    /// yields an empty list.
    /// </param>
    /// <returns>The distinct, valid proxy addresses in first-seen order; never <see langword="null"/>.</returns>
    public static IReadOnlyList<IPAddress> Parse(string? trustedProxies)
    {
        if (string.IsNullOrWhiteSpace(trustedProxies))
            return [];

        var result = new List<IPAddress>();
        var seen = new HashSet<IPAddress>();
        var entries = trustedProxies.Split(
            [',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            if (IPAddress.TryParse(entry, out var address) && seen.Add(address))
                result.Add(address);
        }

        return result;
    }
}
