using Edda.AKG.Ingestion.Connectors;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Connectors;

/// <summary>Unit tests for <see cref="CustomHttpKnowledgeConnector"/>.</summary>
public sealed class CustomHttpKnowledgeConnectorTests
{
    private static (CustomHttpKnowledgeConnector Sut, Mock<IIngestionPipeline> Pipeline) CreateSut(string? token = "tk")
    {
        var pipeline = new Mock<IIngestionPipeline>();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult { Imported = 1 });

        var creds = new Mock<ICredentialStore>();
        creds.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        return (new CustomHttpKnowledgeConnector(pipeline.Object, creds.Object, identity.Object), pipeline);
    }

    private static ConnectorInstanceConfig Instance(params (string Key, string Value)[] values)
        => new()
        {
            Id = "s1",
            TypeId = "custom-http",
            Name = "Test",
            Values = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        };

    [Fact]
    public void TypeId_IsCustomHttp() => CreateSut().Sut.TypeId.Should().Be("custom-http");

    [Fact]
    public void Describe_HasRequiredFields_AndSecretToken()
    {
        var descriptor = CreateSut().Sut.Describe();

        descriptor.Fields.Single(f => f.Key == HttpApiSource.BaseUrlKey).Required.Should().BeTrue();
        descriptor.Fields.Single(f => f.Key == HttpApiSource.ListPathKey).Required.Should().BeTrue();
        descriptor.Fields.Single(f => f.Key == "token").Type.Should().Be(ConnectorFieldType.Secret);
    }

    [Fact]
    public async Task RunAsync_MissingRequired_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut();

        var result = await sut.RunAsync(Instance((HttpApiSource.BaseUrlKey, "https://x")));

        result.Failed.Should().Be(1);
        pipeline.Verify(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_BuildsCustomHttpRequest_WithSettingsAndToken()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut(token: "secret");
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult { Imported = 3 });

        var result = await sut.RunAsync(Instance(
            (HttpApiSource.BaseUrlKey, "https://api.example.com"),
            (HttpApiSource.ListPathKey, "/v1/items"),
            (HttpApiSource.IdFieldKey, "id"),
            ("enrich", "true")));

        result.Imported.Should().Be(3);
        captured.Should().NotBeNull();
        captured!.SourceKind.Should().Be("custom-http");
        captured.EnableEnrichment.Should().BeTrue();
        captured.Source.Token.Should().Be("secret");
        captured.Source.Settings[HttpApiSource.BaseUrlKey].Should().Be("https://api.example.com");
        captured.Source.Settings[HttpApiSource.ListPathKey].Should().Be("/v1/items");
        captured.Source.Settings.Should().NotContainKey("token");
    }
}
