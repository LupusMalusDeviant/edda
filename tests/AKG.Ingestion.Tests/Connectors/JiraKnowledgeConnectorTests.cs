using System.Text;
using Edda.AKG.Ingestion.Connectors;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Connectors;

/// <summary>Unit tests for <see cref="JiraKnowledgeConnector"/>.</summary>
public sealed class JiraKnowledgeConnectorTests
{
    private static (JiraKnowledgeConnector Sut, Mock<IIngestionPipeline> Pipeline) CreateSut(string? token = "tk")
    {
        var pipeline = new Mock<IIngestionPipeline>();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult { Imported = 1 });

        var creds = new Mock<ICredentialStore>();
        creds.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        return (new JiraKnowledgeConnector(pipeline.Object, creds.Object, identity.Object), pipeline);
    }

    private static ConnectorInstanceConfig Instance(params (string Key, string Value)[] values)
        => new()
        {
            Id = "s1",
            TypeId = "jira",
            Name = "Test",
            Values = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        };

    [Fact]
    public void TypeId_IsJira() => CreateSut().Sut.TypeId.Should().Be("jira");

    [Fact]
    public async Task RunAsync_MissingEmail_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut();

        var result = await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net")));

        result.Failed.Should().Be(1);
        pipeline.Verify(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_MissingToken_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut(token: null);

        var result = await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net"), ("email", "a@b.de")));

        result.Failed.Should().Be(1);
        pipeline.Verify(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_BuildsCustomHttpRequest_WithBasicAuthAndSearchPath()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut(token: "api-tk");
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult { Imported = 5 });

        await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net"), ("email", "a@b.de")));

        var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes("a@b.de:api-tk"));
        captured.Should().NotBeNull();
        captured!.SourceKind.Should().Be("custom-http");
        captured.Source.Token.Should().Be(expectedBasic);
        captured.Source.Settings[HttpApiSource.AuthTemplateKey].Should().Be("Basic {token}");
        captured.Source.Settings[HttpApiSource.ListPathKey].Should().StartWith("rest/api/2/search?jql=");
        captured.Source.Settings[HttpApiSource.ItemsPathKey].Should().Be("issues");
        captured.Source.Settings[HttpApiSource.PageModeKey].Should().Be("offset");
    }
}
