using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Models;

/// <summary>Unit tests for the ingestion model defaults and value semantics.</summary>
public sealed class IngestionModelsTests
{
    [Fact]
    public void IngestionItem_Defaults_AreEmptyCollectionsAndNulls()
    {
        var item = new IngestionItem { Id = "x", Title = "t", Body = "b", SourceKind = "git" };

        item.Tags.Should().BeEmpty();
        item.NativeLinks.Should().BeEmpty();
        item.RawFrontmatter.Should().BeEmpty();
        item.SourceUrl.Should().BeNull();
        item.RelativePath.Should().BeNull();
    }

    [Fact]
    public void IngestionLink_CarriesKindAndTarget()
    {
        var link = new IngestionLink { Kind = "supersedes", TargetRef = "git:repo:old" };

        link.Kind.Should().Be("supersedes");
        link.TargetRef.Should().Be("git:repo:old");
    }

    [Fact]
    public void TypeMappingRule_DefaultPriority_IsMedium_AndDomainOptional()
    {
        var rule = new TypeMappingRule { GlobPattern = "docs/**", Type = "WorldKnowledge" };

        rule.Priority.Should().Be(RulePriority.Medium);
        rule.Domain.Should().BeNull();
    }

    [Fact]
    public void IngestionSourceConfig_Defaults_AreEmpty()
    {
        var config = new IngestionSourceConfig();

        config.RepositoryUrl.Should().BeNull();
        config.Reference.Should().BeNull();
        config.IncludeGlobs.Should().BeEmpty();
        config.Settings.Should().BeEmpty();
    }

    [Fact]
    public void IngestionRequest_Defaults_EnrichmentOffAndNoMapping()
    {
        var request = new IngestionRequest { SourceKind = "git", Source = new IngestionSourceConfig() };

        request.EnableEnrichment.Should().BeFalse();
        request.TypeMapping.Should().BeEmpty();
    }

    [Fact]
    public void GitCloneRequest_Reference_IsOptional()
    {
        var request = new GitCloneRequest { RepositoryUrl = "https://gitlab.example/x.git" };

        request.Reference.Should().BeNull();
        request.RepositoryUrl.Should().Be("https://gitlab.example/x.git");
    }

    [Fact]
    public void GitWorkingCopy_CarriesPathAndResolvedReference()
    {
        var copy = new GitWorkingCopy { LocalPath = "/tmp/clone", ResolvedReference = "main" };

        copy.LocalPath.Should().Be("/tmp/clone");
        copy.ResolvedReference.Should().Be("main");
    }
}
