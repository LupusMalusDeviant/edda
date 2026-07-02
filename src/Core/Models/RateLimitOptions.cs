namespace Edda.Core.Models;

/// <summary>
/// Configuration for fixed-window request rate limiting on the public API (<c>/api</c>) and MCP
/// (<c>/mcp</c>) surface. Bound from the <c>EDDA_RATE_LIMIT_PER_MINUTE</c> environment variable.
/// </summary>
public sealed record RateLimitOptions
{
    /// <summary>
    /// Sentinel value that turns rate limiting off, preserving the historical unlimited behaviour.
    /// </summary>
    public const int Disabled = 0;

    /// <summary>
    /// Permitted requests per minute per client IP. Any value at or below <see cref="Disabled"/>
    /// disables rate limiting.
    /// </summary>
    public int PermitsPerMinute { get; init; } = Disabled;

    /// <summary>
    /// Whether rate limiting is active — true only when a positive per-minute limit is configured.
    /// </summary>
    public bool IsEnabled => PermitsPerMinute > Disabled;

    /// <summary>
    /// Parses the raw <c>EDDA_RATE_LIMIT_PER_MINUTE</c> value into options. A <see langword="null"/>,
    /// empty, non-numeric, or non-positive value yields disabled rate limiting (the historical
    /// default), so a misconfiguration never silently throttles traffic.
    /// </summary>
    /// <param name="rawPermitsPerMinute">The raw configuration value, typically an environment variable.</param>
    /// <returns>The resolved <see cref="RateLimitOptions"/>.</returns>
    public static RateLimitOptions Parse(string? rawPermitsPerMinute)
    {
        var permits = int.TryParse(rawPermitsPerMinute, out var parsed) && parsed > Disabled
            ? parsed
            : Disabled;

        return new RateLimitOptions { PermitsPerMinute = permits };
    }
}
