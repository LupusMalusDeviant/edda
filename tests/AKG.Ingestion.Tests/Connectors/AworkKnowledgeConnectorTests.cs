using Edda.AKG.Ingestion.Connectors;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Connectors;

/// <summary>Unit tests for <see cref="AworkKnowledgeConnector"/>.</summary>
public sealed class AworkKnowledgeConnectorTests
{
    private static (AworkKnowledgeConnector Sut, Mock<IIngestionPipeline> Pipeline) CreateSut(string? token = "tk")
    {
        var pipeline = new Mock<IIngestionPipeline>();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult { Imported = 1 });

        var creds = new Mock<ICredentialStore>();
        creds.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        return (new AworkKnowledgeConnector(pipeline.Object, creds.Object, identity.Object), pipeline);
    }

    private static ConnectorInstanceConfig Instance(params (string Key, string Value)[] values)
        => new()
        {
            Id = "s1",
            TypeId = "awork",
            Name = "Test",
            Values = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        };

    [Fact]
    public void TypeId_IsAwork() => CreateSut().Sut.TypeId.Should().Be("awork");

    [Fact]
    public async Task RunAsync_MissingToken_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut(token: null);

        var result = await sut.RunAsync(Instance());

        result.Failed.Should().Be(1);
        pipeline.Verify(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_BuildsCustomHttpRequest_WithBearerAndEntityPath_AndDefaults()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut(token: "api-key");
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult { Imported = 7 });

        await sut.RunAsync(Instance(("entity", "tasks")));

        captured.Should().NotBeNull();
        captured!.SourceKind.Should().Be("custom-http");
        captured.Source.Token.Should().Be("api-key");
        captured.Source.Settings[HttpApiSource.AuthTemplateKey].Should().Be("Bearer {token}");
        captured.Source.Settings[HttpApiSource.ListPathKey].Should().Be("tasks");
        captured.Source.Settings[HttpApiSource.BaseUrlKey].Should().Be("https://api.awork.io/api/v1");
        captured.Source.Settings[HttpApiSource.SourceLabelKey].Should().Be("awork");
        captured.Source.Settings[HttpApiSource.PageModeKey].Should().Be("page");
    }
}
