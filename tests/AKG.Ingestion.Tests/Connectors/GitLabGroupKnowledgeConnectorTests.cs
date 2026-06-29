using Edda.AKG.Ingestion.Connectors;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Connectors;

/// <summary>Unit tests for <see cref="GitLabGroupKnowledgeConnector"/>.</summary>
public sealed class GitLabGroupKnowledgeConnectorTests
{
    private static (GitLabGroupKnowledgeConnector Sut, Mock<IIngestionPipeline> Pipeline) CreateSut(string? token = "tk")
    {
        var pipeline = new Mock<IIngestionPipeline>();
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IngestionResult { Imported = 1 });

        var creds = new Mock<ICredentialStore>();
        creds.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        return (new GitLabGroupKnowledgeConnector(pipeline.Object, creds.Object, identity.Object), pipeline);
    }

    private static ConnectorInstanceConfig Instance(params (string Key, string Value)[] values)
        => new()
        {
            Id = "s1",
            TypeId = "gitlab-group",
            Name = "Test",
            Values = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal),
        };

    [Fact]
    public void TypeId_IsGitLabGroup()
        => CreateSut().Sut.TypeId.Should().Be("gitlab-group");

    [Fact]
    public void Describe_HasExpectedFields()
    {
        var descriptor = CreateSut().Sut.Describe();

        descriptor.TypeId.Should().Be("gitlab-group");
        descriptor.Fields.Select(f => f.Key).Should()
            .Contain(new[] { "gitlabBaseUrl", "groupPath", "includeGlobs", "token", "enrich" });
        descriptor.Fields.Single(f => f.Key == "token").Type.Should().Be(ConnectorFieldType.Secret);
    }

    [Fact]
    public async Task RunAsync_MissingBaseUrlOrGroup_ReturnsFailed()
    {
        var (sut, pipeline) = CreateSut();

        var result = await sut.RunAsync(Instance(("groupPath", "grp")));

        result.Failed.Should().Be(1);
        pipeline.Verify(
            p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_BuildsGroupRequest_WithSettingsAndResolvedToken()
    {
        IngestionRequest? captured = null;
        var (sut, pipeline) = CreateSut(token: "secret-tk");
        pipeline.Setup(p => p.IngestAsync(It.IsAny<IngestionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IngestionRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new IngestionResult { Imported = 2 });

        var result = await sut.RunAsync(Instance(
            ("gitlabBaseUrl", "https://gl.example"),
            ("groupPath", "intern/docs"),
            ("includeGlobs", "docs/**\nspec/**"),
            ("enrich", "true")));

        result.Imported.Should().Be(2);
        captured.Should().NotBeNull();
        captured!.SourceKind.Should().Be("gitlab-group");
        captured.EnableEnrichment.Should().BeTrue();
        captured.Source.Token.Should().Be("secret-tk");
        captured.Source.Settings[GitLabGroupSource.BaseUrlSettingKey].Should().Be("https://gl.example");
        captured.Source.Settings[GitLabGroupSource.GroupSettingKey].Should().Be("intern/docs");
        captured.Source.IncludeGlobs.Should().BeEquivalentTo("docs/**", "spec/**");
    }
}
