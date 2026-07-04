using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Owner-gated management of dataset grants (ADR-0014, Slice 2): the "share" action. Only a dataset Owner or a
/// tenant admin may grant or revoke roles; the tenant and acting user come from the ambient identity, never
/// from arguments. A freshly ingested dataset has no Owner yet, so an admin bootstraps its first grant.
/// </summary>
public interface IDatasetSharingService
{
    /// <summary>Grants (or updates) a role for a target user on a dataset.</summary>
    /// <param name="datasetId">The dataset (provenance head id, e.g. <c>git:my-repo</c>).</param>
    /// <param name="targetUserId">The user to grant the role to.</param>
    /// <param name="role">The role to grant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">When the caller is neither a dataset Owner nor an admin.</exception>
    Task ShareAsync(
        string datasetId, string targetUserId, TenantRole role, CancellationToken cancellationToken = default);

    /// <summary>Revokes a target user's grant on a dataset.</summary>
    /// <param name="datasetId">The dataset (provenance head id).</param>
    /// <param name="targetUserId">The user whose grant is removed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnauthorizedAccessException">When the caller is neither a dataset Owner nor an admin.</exception>
    Task UnshareAsync(string datasetId, string targetUserId, CancellationToken cancellationToken = default);
}
