using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.Web.Services;

/// <summary>
/// Default <see cref="IEntityBrowser"/> (E9): delegates to <see cref="IEntityStore"/> using the ambient user id.
/// A blank query returns nothing (the page prompts for a search) rather than scanning the whole layer, and a
/// missing store (entity layer disabled) yields empty results so the page degrades gracefully.
/// </summary>
public sealed class EntityBrowser : IEntityBrowser
{
    private const int SearchLimit = 50;
    private const int RelatedLimit = 25;

    private readonly IEntityStore? _entities;
    private readonly IIdentityContext? _identity;

    /// <summary>Initializes a new <see cref="EntityBrowser"/>.</summary>
    /// <param name="entities">The entity store; null when the entity layer is disabled.</param>
    /// <param name="identity">Ambient identity supplying the user scope; null returns entities across owners.</param>
    public EntityBrowser(IEntityStore? entities, IIdentityContext? identity = null)
    {
        _entities = entities;
        _identity = identity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphEntity>> SearchAsync(
        string? query, CancellationToken cancellationToken = default)
    {
        var terms = SplitTerms(query);
        if (_entities is null || terms.Count == 0) return [];
        return await _entities.FindEntitiesAsync(terms, _identity?.UserId, SearchLimit, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphEntity>> GetRelatedAsync(
        string entityName, CancellationToken cancellationToken = default)
        => _entities is null
            ? Task.FromResult<IReadOnlyList<GraphEntity>>([])
            : _entities.GetRelatedAsync(entityName, _identity?.UserId, RelatedLimit, cancellationToken);

    /// <summary>Splits a free-text query into distinct, lowercase, whitespace-delimited search terms.</summary>
    /// <param name="query">The raw query; null/blank yields an empty list.</param>
    /// <returns>The distinct lowercase terms.</returns>
    public static IReadOnlyList<string> SplitTerms(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(term => term.ToLowerInvariant())
                   .Distinct(StringComparer.Ordinal)
                   .ToList();
}
