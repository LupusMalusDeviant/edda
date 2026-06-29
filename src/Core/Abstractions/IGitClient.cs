using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Clones a remote Git repository into a local working copy so a file-based ingestion source can read
/// its contents. This is an infrastructure abstraction: implementations may throw on clone failures
/// (network, authentication, missing reference). The ingestion pipeline wraps such failures into an
/// <see cref="IngestionResult"/> rather than propagating them. Credentials are resolved by the
/// implementation (e.g. from the environment), never taken from caller-supplied request data.
/// </summary>
public interface IGitClient
{
    /// <summary>
    /// Clones the requested repository (optionally at a specific branch or tag) into a local directory.
    /// </summary>
    /// <param name="request">The repository URL and optional reference to check out.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local working copy: its path on disk and the resolved reference.</returns>
    Task<GitWorkingCopy> CloneAsync(
        GitCloneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the commit authors of a cloned working copy together with their commit counts, most active
    /// first. Best-effort: returns an empty list when history cannot be read. Author email addresses are
    /// intentionally omitted — only display names and counts are returned.
    /// </summary>
    /// <param name="workingCopy">A working copy previously created by <see cref="CloneAsync"/>.</param>
    /// <returns>Contributors ordered by commit count descending.</returns>
    IReadOnlyList<GitContributor> GetContributors(GitWorkingCopy workingCopy);

    /// <summary>
    /// Removes a working copy previously created by <see cref="CloneAsync"/>, keeping clones transient.
    /// Best-effort: implementations must not throw if the directory is already gone or cannot be fully
    /// removed.
    /// </summary>
    /// <param name="workingCopy">The working copy to delete.</param>
    void Cleanup(GitWorkingCopy workingCopy);
}
