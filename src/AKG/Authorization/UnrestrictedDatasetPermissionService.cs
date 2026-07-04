using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Authorization;

/// <summary>
/// Permissive default <see cref="IDatasetPermissionService"/> (ADR-0014): every caller may read every dataset.
/// It is the behaviour-neutral fallback that keeps the pre-dataset owner/tenant scoping intact — dataset
/// permissions stay inert until a grant-backed service replaces it (a later slice). Stateless and pure.
/// </summary>
internal sealed class UnrestrictedDatasetPermissionService : IDatasetPermissionService
{
    /// <inheritdoc />
    public Task<DatasetVisibility> ResolveVisibilityAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(DatasetVisibility.Unrestricted);
}
