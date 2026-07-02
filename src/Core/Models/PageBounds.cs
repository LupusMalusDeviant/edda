namespace Edda.Core.Models;

/// <summary>
/// Validated pagination bounds (skip/take) for list endpoints such as <c>GET /api/akg/rules</c>.
/// </summary>
public sealed record PageBounds
{
    /// <summary>Hard upper bound on the number of items a single page may return.</summary>
    public const int MaxTake = 1000;

    /// <summary>Page size applied when a caller opts into pagination (supplies skip) without a take.</summary>
    public const int DefaultPageSize = 200;

    /// <summary>Number of leading items to skip.</summary>
    public int Skip { get; init; }

    /// <summary>Maximum number of items to return.</summary>
    public int Take { get; init; }

    /// <summary>
    /// Resolves optional <paramref name="skip"/>/<paramref name="take"/> query values into validated
    /// bounds. With no parameters at all the full set (capped at <see cref="MaxTake"/>) is returned,
    /// preserving the historical unpaged behaviour; supplying <paramref name="skip"/> opts into
    /// pagination and defaults the page size to <see cref="DefaultPageSize"/>.
    /// </summary>
    /// <param name="skip">Optional number of items to skip; must be non-negative when supplied.</param>
    /// <param name="take">Optional page size; must be within <c>[1, MaxTake]</c> when supplied.</param>
    /// <param name="error">
    /// On failure, a caller-facing validation message; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>The resolved bounds, or <see langword="null"/> when validation fails.</returns>
    public static PageBounds? Resolve(int? skip, int? take, out string? error)
    {
        if (skip is < 0)
        {
            error = "skip must be greater than or equal to 0.";
            return null;
        }

        if (take is < 1 or > MaxTake)
        {
            error = $"take must be between 1 and {MaxTake}.";
            return null;
        }

        error = null;
        return new PageBounds
        {
            Skip = skip ?? 0,
            Take = take ?? (skip.HasValue ? DefaultPageSize : MaxTake),
        };
    }
}
