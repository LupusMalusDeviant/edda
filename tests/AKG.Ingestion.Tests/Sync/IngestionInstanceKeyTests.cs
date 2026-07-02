using Edda.AKG.Ingestion.Sync;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Sync;

/// <summary>Unit tests for <see cref="IngestionInstanceKey"/> (C5 per-instance key).</summary>
public sealed class IngestionInstanceKeyTests
{
    private static IngestionRequest GitRequest(string? repo = "https://host/r.git", string? canonical = null)
        => new()
        {
            SourceKind = "git",
            Source = new IngestionSourceConfig { RepositoryUrl = repo, CanonicalUrl = canonical },
        };

    private static IngestionRequest HttpRequest(string baseUrl, string listPath, string label = "custom-http")
        => new()
        {
            SourceKind = "custom-http",
            Source = new IngestionSourceConfig
            {
                Settings = new Dictionary<string, string>
                {
                    ["baseUrl"] = baseUrl,
                    ["listPath"] = listPath,
                    ["sourceLabel"] = label,
                },
            },
        };

    [Fact]
    public void For_SameRequest_ReturnsSameKey()
        => IngestionInstanceKey.For(GitRequest()).Should().Be(IngestionInstanceKey.For(GitRequest()));

    [Fact]
    public void For_DifferentRepositories_ReturnDifferentKeys()
        => IngestionInstanceKey.For(GitRequest("https://host/a.git"))
            .Should().NotBe(IngestionInstanceKey.For(GitRequest("https://host/b.git")));

    [Fact]
    public void For_CanonicalUrlPreferredOverRepositoryUrl()
    {
        var withCanonical = IngestionInstanceKey.For(GitRequest("https://mirror/r.git", canonical: "https://origin/r.git"));
        var byOrigin = IngestionInstanceKey.For(GitRequest("https://origin/r.git"));
        withCanonical.Should().Be(byOrigin);
    }

    [Fact]
    public void For_CustomHttp_DistinguishesByListPath()
        => IngestionInstanceKey.For(HttpRequest("https://api", "issues"))
            .Should().NotBe(IngestionInstanceKey.For(HttpRequest("https://api", "epics")));

    [Fact]
    public void For_DifferentSourceKind_ProducesDifferentKey()
    {
        IngestionInstanceKey.For(GitRequest()).Should().StartWith("git|");
        IngestionInstanceKey.For(HttpRequest("https://api", "issues")).Should().StartWith("custom-http|");
    }
}
