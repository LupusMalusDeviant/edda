using Edda.AKG.Mcp.Knowledge;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Mcp.Tests.Knowledge;

/// <summary>Unit tests for <see cref="McpKnowledgeConnector"/>.</summary>
public sealed class McpKnowledgeConnectorTests
{
    private static (McpKnowledgeConnector Sut, Mock<IIngestionPipeline> Pipeline) CreateSut(string? token = "tk")
    {
        var pipeline = new Mock<IIngestionPipeline>();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult { Imported = 1 });

        var creds = new Mock<ICredentialStore>();
        creds.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        return (new McpKnowledgeConnector(pipeline.Object, creds.Object, identity.Object), pipeline);
    }

    private static ConnectorInstanceConfig Instance(params (string Key, string Value)[] values)
        => new()
        {
            Id = "s1",
            TypeId = "mcp",
            Name = "Test",
            Values = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        };

    [Fact]
    public void TypeId_IsMcp() => CreateSut().Sut.TypeId.Should().Be("mcp");

    [Fact]
    public void Describe_HasServerAndTool_AndSecretToken()
    {
        var descriptor = CreateSut().Sut.Describe();

        descriptor.Fields.Single(f => f.Key == McpKnowledgeSource.ServerUrlKey).Required.Should().BeTrue();
        descriptor.Fields.Single(f => f.Key == McpKnowledgeSource.ToolNameKey).Required.Should().BeTrue();
        descriptor.Fields.Single(f => f.Key == "token").Type.Should().Be(ConnectorFieldType.Secret);
    }

    [Fact]
    public async Task RunAsync_MissingRequired_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut();

        var result = await sut.RunAsync(Instance((McpKnowledgeSource.ServerUrlKey, "https://x")));

        result.Failed.Should().Be(1);
        pipeline.Verify(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_BuildsMcpRequest_WithSettingsAndToken()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut(token: "secret");
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult { Imported = 2 });

        await sut.RunAsync(Instance(
            (McpKnowledgeSource.ServerUrlKey, "https://mcp.example"),
            (McpKnowledgeSource.ToolNameKey, "search"),
            ("enrich", "true")));

        captured.Should().NotBeNull();
        captured!.SourceKind.Should().Be("mcp");
        captured.EnableEnrichment.Should().BeTrue();
        captured.Source.Token.Should().Be("secret");
        captured.Source.Settings[McpKnowledgeSource.ServerUrlKey].Should().Be("https://mcp.example");
        captured.Source.Settings[McpKnowledgeSource.ToolNameKey].Should().Be("search");
    }
}
