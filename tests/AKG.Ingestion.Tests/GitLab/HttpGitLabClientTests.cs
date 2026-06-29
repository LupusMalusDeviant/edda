using Edda.AKG.Ingestion.GitLab;

namespace Edda.AKG.Ingestion.Tests.GitLab;

/// <summary>Unit tests for <see cref="HttpGitLabClient"/> JSON parsing.</summary>
public sealed class HttpGitLabClientTests
{
    [Fact]
    public void ParseCloneUrls_ExtractsHttpUrlToRepo()
    {
        var json =
            """
            [
              { "id": 1, "name": "a", "http_url_to_repo": "https://gl.example/grp/a.git" },
              { "id": 2, "name": "b", "http_url_to_repo": "https://gl.example/grp/b.git" }
            ]
            """;

        var urls = HttpGitLabClient.ParseCloneUrls(json);

        urls.Should().BeEquivalentTo("https://gl.example/grp/a.git", "https://gl.example/grp/b.git");
    }

    [Fact]
    public void ParseCloneUrls_EmptyArray_ReturnsEmpty()
    {
        HttpGitLabClient.ParseCloneUrls("[]").Should().BeEmpty();
    }

    [Fact]
    public void ParseCloneUrls_NonArrayPayload_ReturnsEmpty()
    {
        HttpGitLabClient.ParseCloneUrls("""{ "message": "404 Group Not Found" }""").Should().BeEmpty();
    }
}
