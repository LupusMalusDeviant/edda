using Edda.AKG.Ingestion.Connectors;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Connectors;

/// <summary>Unit tests for <see cref="GitKnowledgeConnector"/> request building and token resolution.</summary>
public sealed class GitKnowledgeConnectorTests
{
    private static (GitKnowledgeConnector Sut, Mock<IIngestionPipeline> Pipeline) CreateSut(string? token = null)
    {
        var pipeline = new Mock<IIngestionPipeline>();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult { Imported = 1 });

        var credentials = new Mock<ICredentialStore>();
        credentials.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        if (token is not null)
        {
            credentials.Setup(c => c.RetrieveAsync("local:source:src1:token", It.IsAny<CancellationToken>()))
                .ReturnsAsync(token);
        }

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        return (new GitKnowledgeConnector(pipeline.Object, credentials.Object, identity.Object), pipeline);
    }

    private static ConnectorInstanceConfig Instance(IReadOnlyDictionary<string, string> values) =>
        new() { Id = "src1", TypeId = "git", Name = "Repo", Values = values };

    [Fact]
    public void TypeId_IsGit() => CreateSut().Sut.TypeId.Should().Be("git");

    [Fact]
    public void Describe_HasSecretTokenField()
    {
        var descriptor = CreateSut().Sut.Describe();

        descriptor.TypeId.Should().Be("git");
        descriptor.Fields.Should().Contain(f => f.Key == "token" && f.Type == ConnectorFieldType.Secret);
        descriptor.Fields.Should().Contain(f => f.Key == "repoUrl" && f.Required);
    }

    [Fact]
    public async Task RunAsync_BuildsGitRequest_WithResolvedToken_AndDelegates()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut(token: "tok");
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult { Imported = 4 });

        var result = await sut.RunAsync(Instance(new Dictionary<string, string>
        {
            ["repoUrl"] = "https://git.example.com/x.git",
            ["reference"] = "main",
            ["includeGlobs"] = "docs/**\nadr/**",
            ["enrich"] = "true",
        }));

        result.Imported.Should().Be(4);
        captured.Should().NotBeNull();
        captured!.SourceKind.Should().Be("git");
        captured.Source.RepositoryUrl.Should().Be("https://git.example.com/x.git");
        captured.Source.Reference.Should().Be("main");
        captured.Source.IncludeGlobs.Should().BeEquivalentTo(["docs/**", "adr/**"]);
        captured.Source.Token.Should().Be("tok");
        captured.EnableEnrichment.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_MissingRepoUrl_ReturnsFailed_WithoutCallingPipeline()
    {
        var (sut, pipeline) = CreateSut();

        var result = await sut.RunAsync(Instance(new Dictionary<string, string>()));

        result.Failed.Should().Be(1);
        pipeline.Verify(
            p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
