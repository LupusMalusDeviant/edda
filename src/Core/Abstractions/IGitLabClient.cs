namespace Edda.Core.Abstractions;

/// <summary>
/// Minimal GitLab API client used by group-based ingestion. Resolves the clone URLs of all projects
/// within a group (recursively including subgroups) so each repository can then be ingested via the
/// regular Git source.
/// </summary>
public interface IGitLabClient
{
    /// <summary>
    /// Lists the clone URLs of every (non-archived) project in a group, including subgroups.
    /// </summary>
    /// <param name="groupPath">The group's full path (e.g. <c>acme/team</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTTPS clone URLs of the group's projects.</returns>
    Task<IReadOnlyList<string>> ListGroupProjectCloneUrlsAsync(
        string groupPath,
        CancellationToken cancellationToken = default);
}
