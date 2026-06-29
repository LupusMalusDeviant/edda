using Edda.AKG.Ingestion.Sources;
using Edda.AKG.Ingestion.Tests.TestUtilities;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Sources;

/// <summary>Unit tests for <see cref="GitLabGroupSource"/>.</summary>
public sealed class GitLabGroupSourceTests
{
    private static InMemoryFileSystem RepoFs()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("repo/README.md", "# Title\n\nBody.");
        return fs;
    }

    private static GitLabGroupSource Build(FakeGitLabClient gitLab, out FakeGitLabClientFactory factory)
    {
        factory = new FakeGitLabClientFactory(gitLab);
        return new GitLabGroupSource(factory, new GitMarkdownSource(new FakeGitClient("repo"), RepoFs()));
    }

    private static async Task<List<IngestionItem>> Collect(GitLabGroupSource source, IngestionSourceConfig config)
    {
        var items = new List<IngestionItem>();
        await foreach (var item in source.FetchAsync(config))
            items.Add(item);
        return items;
    }

    private static IngestionSourceConfig WithGroup(string group, string? token = null)
        => new()
        {
            Token = token,
            Settings = new Dictionary<string, string>
            {
                [GitLabGroupSource.GroupSettingKey] = group,
                [GitLabGroupSource.BaseUrlSettingKey] = "https://gl.example",
            },
        };

    [Fact]
    public async Task FetchAsync_IteratesAllGroupRepos()
    {
        var gitLab = new FakeGitLabClient(
            "https://gl.example/grp/repo-a.git",
            "https://gl.example/grp/repo-b.git");
        var source = Build(gitLab, out _);

        var items = await Collect(source, WithGroup("grp"));

        // Both repos plus their shared host/group nodes are emitted (the pipeline later de-duplicates).
        items.Select(i => i.Id).Should().Contain(new[]
        {
            "git-host:gl.example", "git-group:grp",
            "git:repo-a", "git:repo-a:README",
            "git:repo-b", "git:repo-b:README",
        });
        gitLab.LastGroup.Should().Be("grp");
    }

    [Fact]
    public async Task FetchAsync_ResolvesClientFromBaseUrlAndToken()
    {
        var gitLab = new FakeGitLabClient("https://gl.example/grp/repo-a.git");
        var source = Build(gitLab, out var factory);

        await Collect(source, WithGroup("grp", token: "tk"));

        factory.LastBaseUrl.Should().Be("https://gl.example");
        factory.LastToken.Should().Be("tk");
    }

    [Fact]
    public async Task FetchAsync_NoGroupSetting_YieldsNothing()
    {
        var gitLab = new FakeGitLabClient("https://gl.example/grp/repo-a.git");
        var source = Build(gitLab, out _);

        var items = await Collect(source, new IngestionSourceConfig());

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_NoBaseUrlSetting_YieldsNothing()
    {
        var gitLab = new FakeGitLabClient("https://gl.example/grp/repo-a.git");
        var source = Build(gitLab, out _);
        var config = new IngestionSourceConfig
        {
            Settings = new Dictionary<string, string> { [GitLabGroupSource.GroupSettingKey] = "grp" },
        };

        var items = await Collect(source, config);

        items.Should().BeEmpty();
    }

    [Fact]
    public void SourceKind_IsGitLabGroup()
    {
        var source = Build(new FakeGitLabClient(), out _);

        source.SourceKind.Should().Be("gitlab-group");
    }
}
