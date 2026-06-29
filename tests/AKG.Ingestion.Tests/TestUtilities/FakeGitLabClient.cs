using Edda.Core.Abstractions;

namespace Edda.AKG.Ingestion.Tests.TestUtilities;

/// <summary>Fake <see cref="IGitLabClient"/> returning a fixed set of clone URLs.</summary>
internal sealed class FakeGitLabClient : IGitLabClient
{
    private readonly IReadOnlyList<string> _cloneUrls;

    public FakeGitLabClient(params string[] cloneUrls) => _cloneUrls = cloneUrls;

    public string? LastGroup { get; private set; }

    public Task<IReadOnlyList<string>> ListGroupProjectCloneUrlsAsync(
        string groupPath,
        CancellationToken cancellationToken = default)
    {
        LastGroup = groupPath;
        return Task.FromResult(_cloneUrls);
    }
}
