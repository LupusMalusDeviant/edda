using Edda.AKG.Ingestion.GitLab;
using Moq;

namespace Edda.AKG.Ingestion.Tests.GitLab;

/// <summary>Unit tests for <see cref="HttpGitLabClientFactory"/>.</summary>
public sealed class HttpGitLabClientFactoryTests
{
    [Fact]
    public void Create_ReturnsHttpGitLabClient()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var sut = new HttpGitLabClientFactory(httpFactory.Object);

        var client = sut.Create("https://gl.example", "tk");

        client.Should().BeOfType<HttpGitLabClient>();
    }
}
