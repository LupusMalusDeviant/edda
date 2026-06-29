using Edda.Core.Abstractions;
using Edda.Core.Models;
using LibGit2Sharp;

namespace Edda.AKG.Ingestion.Git;

/// <summary>
/// <see cref="IGitClient"/> backed by LibGit2Sharp (see ADR-0002). Clones a remote repository into a
/// per-repository directory under a configurable cache root, performing a fresh clone on each run.
/// <para>
/// This is an infrastructure adapter and the deliberate, documented exception to the "no direct file
/// I/O" rule (like the physical file system): LibGit2Sharp and clone-directory management operate
/// directly on disk and cannot be expressed through <see cref="IFileSystem"/>. Credentials are read
/// from configuration/environment, never from caller-supplied request data. Because it wraps a native
/// library, it is covered by an optional integration test rather than unit tests.
/// </para>
/// </summary>
public sealed class LibGit2SharpGitClient : IGitClient
{
    private readonly string _cacheRoot;
    private readonly string? _username;
    private readonly string? _token;

    /// <summary>Initializes a new instance of the <see cref="LibGit2SharpGitClient"/> class.</summary>
    /// <param name="cacheRoot">Root directory under which repositories are cloned.</param>
    /// <param name="username">Optional username for authenticated clones (defaults to "oauth2").</param>
    /// <param name="token">Optional access token for private repositories.</param>
    public LibGit2SharpGitClient(string cacheRoot, string? username = null, string? token = null)
    {
        _cacheRoot = cacheRoot;
        _username = username;
        _token = token;
    }

    /// <inheritdoc />
    public async Task<GitWorkingCopy> CloneAsync(
        GitCloneRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => Clone(request), cancellationToken).ConfigureAwait(false);
    }

    private GitWorkingCopy Clone(GitCloneRequest request)
    {
        var slug = GitItemIdentity.Slug(request.RepositoryUrl);

        // Infrastructure-level direct I/O (see class remarks): LibGit2Sharp needs a real on-disk path.
        var workingDirectory = System.IO.Path.Combine(_cacheRoot, slug);
        DeleteDirectoryRobust(workingDirectory);
        System.IO.Directory.CreateDirectory(_cacheRoot);

        var options = new CloneOptions();
        if (!string.IsNullOrWhiteSpace(request.Reference))
            options.BranchName = request.Reference;

        // A per-request token (supplied server-side by a connector from the credential store) wins over
        // the client's configured default; same for the username.
        var token = string.IsNullOrWhiteSpace(request.Token) ? _token : request.Token;
        if (!string.IsNullOrWhiteSpace(token))
        {
            var configuredUser = string.IsNullOrWhiteSpace(request.Username) ? _username : request.Username;
            var user = string.IsNullOrWhiteSpace(configuredUser) ? "oauth2" : configuredUser;
            options.FetchOptions.CredentialsProvider = (_, _, _) =>
                new UsernamePasswordCredentials { Username = user, Password = token };
        }

        Repository.Clone(request.RepositoryUrl, workingDirectory, options);

        using var repository = new Repository(workingDirectory);
        var resolvedReference = repository.Head.FriendlyName;

        return new GitWorkingCopy
        {
            LocalPath = workingDirectory,
            ResolvedReference = string.IsNullOrWhiteSpace(resolvedReference) ? request.Reference : resolvedReference,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<GitContributor> GetContributors(GitWorkingCopy workingCopy)
    {
        try
        {
            using var repository = new Repository(workingCopy.LocalPath);
            return repository.Commits
                .Where(commit => !string.IsNullOrWhiteSpace(commit.Author?.Name))
                .GroupBy(commit => commit.Author!.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new GitContributor(group.Key, group.Count()))
                .OrderByDescending(contributor => contributor.Commits)
                .ThenBy(contributor => contributor.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (LibGit2SharpException)
        {
            // History unreadable (e.g. an empty or corrupt clone) — contributors are best-effort metadata.
            return [];
        }
    }

    /// <inheritdoc />
    public void Cleanup(GitWorkingCopy workingCopy)
    {
        try
        {
            DeleteDirectoryRobust(workingCopy.LocalPath);
        }
        catch (System.IO.IOException)
        {
            // Best-effort cleanup — a leftover clone must never fail the ingestion.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup — a leftover clone must never fail the ingestion.
        }
    }

    private static void DeleteDirectoryRobust(string path)
    {
        if (!System.IO.Directory.Exists(path))
            return;

        // Cloned ".git" pack files are typically read-only; clear attributes so Delete can remove them.
        foreach (var file in System.IO.Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories))
            System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);

        System.IO.Directory.Delete(path, recursive: true);
    }
}
