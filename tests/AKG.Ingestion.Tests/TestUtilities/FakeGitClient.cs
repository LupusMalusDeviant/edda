using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.TestUtilities;

/// <summary>
/// Fake <see cref="IGitClient"/> for unit tests. Returns a fixed local working-copy path and records
/// the last clone request so tests can assert on it. No real cloning occurs.
/// </summary>
internal sealed class FakeGitClient : IGitClient
{
    private readonly string _localPath;

    public FakeGitClient(string localPath) => _localPath = localPath;

    public GitCloneRequest? LastRequest { get; private set; }

    public Task<GitWorkingCopy> CloneAsync(GitCloneRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(new GitWorkingCopy
        {
            LocalPath = _localPath,
            ResolvedReference = request.Reference,
        });
    }

    /// <summary>Contributors returned by <see cref="GetContributors"/>; empty unless a test sets them.</summary>
    public IReadOnlyList<GitContributor> Contributors { get; set; } = [];

    public IReadOnlyList<GitContributor> GetContributors(GitWorkingCopy workingCopy) => Contributors;

    public int CleanupCount { get; private set; }

    public void Cleanup(GitWorkingCopy workingCopy) => CleanupCount++;
}
