using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Orchestrates the import and synchronization of ASPS.ai projects into the AKG.
/// Creates a dedicated AKG domain per project, parses the Lastenheft into knowledge rules,
/// and maintains the mapping between ASPS slugs and internal project state.
/// </summary>
public interface IAspsProjectSync
{
    /// <summary>
    /// Imports a project from ASPS.ai: fetches Lastenheft, creates AKG domain,
    /// parses chapters into knowledge rules, and imports tasks as graph nodes.
    /// </summary>
    /// <param name="aspsSlug">The ASPS.ai project slug to import.</param>
    /// <param name="userId">The user who initiated the import.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Import result with rule/task counts and the created AKG domain name.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the project is already imported.</exception>
    Task<AspsImportResult> ImportProjectAsync(
        string aspsSlug,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Re-fetches the Lastenheft from ASPS.ai and updates AKG rules if the version changed.
    /// New chapters produce new rules; changed chapters update existing rules; removed content
    /// deactivates (but does not delete) rules.
    /// </summary>
    /// <param name="aspsSlug">The ASPS.ai project slug to sync.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Sync result indicating whether content changed and how many rules were affected.</returns>
    Task<AspsSyncResult> SyncProjectAsync(
        string aspsSlug,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all ASPS.ai projects that have been imported for a given user.
    /// </summary>
    /// <param name="userId">The user whose imported projects to list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of imported project summaries.</returns>
    Task<IReadOnlyList<AspsImportedProject>> ListImportedAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an imported ASPS.ai project: removes all associated AKG rules,
    /// deletes the AKG domain, and removes the project mapping from the store.
    /// </summary>
    /// <param name="aspsSlug">The ASPS.ai project slug to delete.</param>
    /// <param name="userId">The user requesting the deletion.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if the project is not imported.</exception>
    Task DeleteProjectAsync(
        string aspsSlug,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves an ASPS.ai slug to the internal project id used by all
    /// pipeline components (AKG domain, prototype state, project log store).
    /// Returns null if no mapping exists.
    /// </summary>
    /// <param name="aspsSlug">The ASPS.ai project slug.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> GetInternalProjectIdBySlugAsync(
        string aspsSlug,
        CancellationToken ct = default);
}
