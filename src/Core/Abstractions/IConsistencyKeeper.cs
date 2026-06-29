using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Merges multiple sub-agent file contributions into a single versioned
/// prototype directory while preserving byte-identical copies of files that
/// did not change between versions. Produces a persisted <see cref="ArtefactManifest"/>.
/// </summary>
public interface IConsistencyKeeper
{
    /// <summary>
    /// Merges the given contributions into <see cref="ConsistencyMergeRequest.OutputDirectory"/>.
    /// Files present in <see cref="ConsistencyMergeRequest.PreviousManifest"/> that are not
    /// overwritten by any contribution are copied from
    /// <see cref="ConsistencyMergeRequest.PreviousDirectory"/> byte-identically; their
    /// manifest entry (owner, SHA-256, created-at) is preserved.
    /// </summary>
    /// <param name="request">Merge request with contributions and optional previous version info.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The assembled manifest; also persisted as <c>manifest.json</c> in the output directory.</returns>
    /// <exception cref="MergeConflictException">Two contributions wrote the same file path.</exception>
    Task<ArtefactManifest> MergeAsync(
        ConsistencyMergeRequest request,
        CancellationToken ct);

    /// <summary>
    /// Loads the manifest from <c>{directoryPath}/manifest.json</c>, or returns
    /// <c>null</c> if the file does not exist.
    /// </summary>
    /// <param name="directoryPath">Directory containing <c>manifest.json</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Deserialized manifest, or null if missing.</returns>
    Task<ArtefactManifest?> LoadManifestAsync(
        string directoryPath,
        CancellationToken ct);
}
