using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Manages the AKG domain hierarchy. Domains organize rules into semantic categories.
/// New domains can be added without code changes — they are stored in Neo4j.
/// </summary>
public interface IDomainManager
{
    /// <summary>
    /// Returns the full domain tree from root to leaves.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All domains including their parent/child relationships.</returns>
    Task<IReadOnlyList<DomainNode>> GetDomainTreeAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new domain node in the hierarchy.
    /// </summary>
    /// <param name="name">Unique domain name (lowercase, kebab-case).</param>
    /// <param name="label">Human-readable display label.</param>
    /// <param name="parentDomain">Name of the parent domain. Null for root-level domains.</param>
    /// <param name="description">Optional description of the domain's scope.</param>
    /// <param name="ownerId">Owning user ID, or null for system/global domains.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created domain node.</returns>
    Task<DomainNode> CreateDomainAsync(
        string name,
        string label,
        string? parentDomain,
        string? description,
        string? ownerId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a domain and removes it from the hierarchy.
    /// Rules belonging to the domain are not automatically deleted.
    /// </summary>
    /// <param name="name">Name of the domain to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteDomainAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the specified domain name exists in the graph.
    /// </summary>
    /// <param name="name">Domain name to check.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> ExistsAsync(string name, CancellationToken ct = default);
}
