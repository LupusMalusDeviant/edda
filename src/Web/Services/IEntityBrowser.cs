using Edda.Core.Models;

namespace Edda.Web.Services;

/// <summary>
/// UI-facing read facade over the graph entity layer (E9): searches the current user's entities and returns an
/// entity's 1-hop <c>RELATES_TO</c> neighbours. The user scope comes from the ambient identity (Regel 7), never
/// from UI input. When the entity layer is disabled (no store), both operations return an empty list.
/// </summary>
public interface IEntityBrowser
{
    /// <summary>Searches the current user's entities by a free-text query (whitespace-split into terms).</summary>
    /// <param name="query">The free-text search query; blank yields an empty result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching entities (empty when the query is blank or the entity layer is off).</returns>
    Task<IReadOnlyList<GraphEntity>> SearchAsync(string? query, CancellationToken cancellationToken = default);

    /// <summary>Returns the 1-hop <c>RELATES_TO</c> neighbours of the named entity, scoped to the current user.</summary>
    /// <param name="entityName">The centre entity's name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The neighbouring entities (empty when the entity layer is off).</returns>
    Task<IReadOnlyList<GraphEntity>> GetRelatedAsync(string entityName, CancellationToken cancellationToken = default);
}
