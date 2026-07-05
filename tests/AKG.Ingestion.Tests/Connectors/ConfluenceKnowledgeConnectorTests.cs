using System.Text;
using Edda.AKG.Ingestion.Connectors;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Connectors;

/// <summary>Unit tests for <see cref="ConfluenceKnowledgeConnector"/>.</summary>
public sealed class ConfluenceKnowledgeConnectorTests
{
    private static (ConfluenceKnowledgeConnector Sut, Mock<IIngestionPipeline> Pipeline) CreateSut(string? token = "tk")
    {
        var pipeline = new Mock<IIngestionPipeline>();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult { Imported = 1 });

        var creds = new Mock<ICredentialStore>();
        creds.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        return (new ConfluenceKnowledgeConnector(pipeline.Object, creds.Object, identity.Object), pipeline);
    }

    private static ConnectorInstanceConfig Instance(params (string Key, string Value)[] values)
        => new()
        {
            Id = "s1",
            TypeId = "confluence",
            Name = "Test",
            Values = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        };

    [Fact]
    public void TypeId_IsConfluence() => CreateSut().Sut.TypeId.Should().Be("confluence");

    [Fact]
    public void Describe_ExposesRequiredFields()
    {
        var descriptor = CreateSut().Sut.Describe();

        descriptor.TypeId.Should().Be("confluence");
        descriptor.Fields.Should().Contain(f => f.Key == "baseUrl" && f.Required);
        descriptor.Fields.Should().Contain(f => f.Key == "email" && f.Required);
        descriptor.Fields.Should().Contain(f => f.Key == "token" && f.Type == ConnectorFieldType.Secret);
        descriptor.Fields.Should().Contain(f => f.Key == "cql");
    }

    [Fact]
    public async Task RunAsync_MissingEmail_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut();

        var result = await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net/wiki")));

        result.Failed.Should().Be(1);
        pipeline.Verify(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_MissingToken_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut(token: null);

        var result = await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net/wiki"), ("email", "a@b.de")));

        result.Failed.Should().Be(1);
        pipeline.Verify(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_BuildsCustomHttpRequest_WithBasicAuthAndContentSearchPath()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut(token: "api-tk");
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult { Imported = 5 });

        await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net/wiki"), ("email", "a@b.de")));

        var expectedBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes("a@b.de:api-tk"));
        captured.Should().NotBeNull();
        captured!.SourceKind.Should().Be("custom-http");
        captured.Source.Token.Should().Be(expectedBasic);
        captured.Source.Settings[HttpApiSource.AuthTemplateKey].Should().Be("Basic {token}");
        captured.Source.Settings[HttpApiSource.ListPathKey].Should().StartWith("rest/api/content/search?cql=");
        captured.Source.Settings[HttpApiSource.ItemsPathKey].Should().Be("results");
        captured.Source.Settings[HttpApiSource.BodyFieldKey].Should().Be("body.storage.value");
        captured.Source.Settings[HttpApiSource.PageModeKey].Should().Be("offset");
        captured.Source.Settings[HttpApiSource.PageParamKey].Should().Be("start");
        captured.Source.Settings[HttpApiSource.PageSizeParamKey].Should().Be("limit");
    }

    [Fact]
    public async Task RunAsync_NoCql_UsesDefaultCql()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult());

        await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net/wiki"), ("email", "a@b.de")));

        captured!.Source.Settings[HttpApiSource.ListPathKey]
            .Should().Contain(Uri.EscapeDataString("type=page ORDER BY lastmodified DESC"));
    }

    [Fact]
    public async Task RunAsync_CustomCql_IsEscapedIntoListPath()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult());

        await sut.RunAsync(Instance(
            ("baseUrl", "https://x.atlassian.net/wiki"), ("email", "a@b.de"), ("cql", "space=DEV")));

        captured!.Source.Settings[HttpApiSource.ListPathKey].Should().Contain(Uri.EscapeDataString("space=DEV"));
    }

    [Fact]
    public async Task RunAsync_EnrichTrue_EnablesEnrichment()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult());

        await sut.RunAsync(Instance(
            ("baseUrl", "https://x.atlassian.net/wiki"), ("email", "a@b.de"), ("enrich", "true")));

        captured!.EnableEnrichment.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_PipelineThrows_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await sut.RunAsync(Instance(("baseUrl", "https://x.atlassian.net/wiki"), ("email", "a@b.de")));

        result.Failed.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("boom");
    }
}
