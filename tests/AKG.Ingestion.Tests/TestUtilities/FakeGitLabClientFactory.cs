using Edda.Core.Abstractions;

namespace Edda.AKG.Ingestion.Tests.TestUtilities;

/// <summary>Fake <see cref="IGitLabClientFactory"/> returning a fixed client and recording the inputs.</summary>
internal sealed class FakeGitLabClientFactory : IGitLabClientFactory
{
    private readonly IGitLabClient _client;

    public FakeGitLabClientFactory(IGitLabClient client) => _client = client;

    public string? LastBaseUrl { get; private set; }

    public string? LastToken { get; private set; }

    public IGitLabClient Create(string baseUrl, string? token)
    {
        LastBaseUrl = baseUrl;
        LastToken = token;
        return _client;
    }
}
