namespace Edda.Core.Models;

/// <summary>A request to clone a Git repository into a local working copy.</summary>
public sealed record GitCloneRequest
{
    /// <summary>The repository URL to clone (e.g. an HTTPS GitLab URL).</summary>
    public required string RepositoryUrl { get; init; }

    /// <summary>Optional branch or tag to check out; null uses the repository default branch.</summary>
    public string? Reference { get; init; }

    /// <summary>Optional username for an authenticated clone; null uses the client's configured default.</summary>
    public string? Username { get; init; }

    /// <summary>Optional access token for an authenticated clone; null uses the client's configured default.</summary>
    public string? Token { get; init; }
}

/// <summary>A local checkout of a remote repository produced by <c>IGitClient</c>.</summary>
public sealed record GitWorkingCopy
{
    /// <summary>Absolute path to the checked-out working copy on disk.</summary>
    public required string LocalPath { get; init; }

    /// <summary>The reference (branch/tag/commit) that was actually checked out, if known.</summary>
    public string? ResolvedReference { get; init; }
}
