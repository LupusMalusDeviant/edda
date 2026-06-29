using Edda.AKG.Ingestion.Sources;
using Edda.AKG.Ingestion.Tests.TestUtilities;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Sources;

/// <summary>Unit tests for <see cref="GitMarkdownSource"/> using fake Git and file-system clients.</summary>
public sealed class GitMarkdownSourceTests
{
    private const string RepoUrl = "https://gitlab.example/group/my-repo.git";

    private static InMemoryFileSystem BuildRepo()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("repo/README.md",
            """
            # My Project

            See [the guide](docs/guide.md).
            """);
        fs.AddFile("repo/docs/guide.md",
            """
            ---
            title: User Guide
            ---
            Guide body.
            """);
        fs.AddFile("repo/docs/adr/0001-foo.md",
            """
            # First Decision

            Superseded — see [ADR 2](0002-bar.md).
            """);
        fs.AddFile("repo/docs/adr/0002-bar.md", "# Second Decision");
        fs.AddFile("repo/notes/plain.md", "Just plain text, no heading.");
        return fs;
    }

    private static async Task<List<IngestionItem>> Collect(
        GitMarkdownSource source, IngestionSourceConfig config)
    {
        var items = new List<IngestionItem>();
        await foreach (var item in source.FetchAsync(config))
            items.Add(item);
        return items;
    }

    private static GitMarkdownSource CreateSource(InMemoryFileSystem fs)
        => new(new FakeGitClient("repo"), fs);

    [Fact]
    public async Task FetchAsync_ClonesAndYieldsAllMarkdownFiles()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        items.Select(i => i.Id).Should().BeEquivalentTo(
            "git-knowledge",
            "git-host:gitlab.example",
            "git-group:group",
            "git:my-repo",
            "git:my-repo:README",
            "git:my-repo:docs/guide",
            "git:my-repo:docs/adr/0001-foo",
            "git:my-repo:docs/adr/0002-bar",
            "git:my-repo:notes/plain");
    }

    [Fact]
    public async Task FetchAsync_UsesFrontmatterTitleWhenPresent()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        items.Single(i => i.Id == "git:my-repo:docs/guide").Title.Should().Be("User Guide");
    }

    [Fact]
    public async Task FetchAsync_FallsBackToFirstHeading()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        items.Single(i => i.Id == "git:my-repo:README").Title.Should().Be("My Project");
    }

    [Fact]
    public async Task FetchAsync_FallsBackToFileNameWhenNoTitleOrHeading()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        items.Single(i => i.Id == "git:my-repo:notes/plain").Title.Should().Be("plain");
    }

    [Fact]
    public async Task FetchAsync_DerivesPathTags()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        items.Single(i => i.Id == "git:my-repo:docs/guide").Tags.Should().Contain("docs");
        items.Single(i => i.Id == "git:my-repo:docs/adr/0001-foo").Tags.Should().Contain("adr");
        items.Single(i => i.Id == "git:my-repo:README").Tags.Should().Contain("readme");
    }

    [Fact]
    public async Task FetchAsync_ResolvesRelativeLinksToStableIds()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        items.Single(i => i.Id == "git:my-repo:README")
            .NativeLinks.Select(l => l.TargetRef).Should().Contain("git:my-repo:docs/guide");

        items.Single(i => i.Id == "git:my-repo:docs/adr/0001-foo")
            .NativeLinks.Select(l => l.TargetRef).Should().Contain("git:my-repo:docs/adr/0002-bar");
    }

    [Fact]
    public async Task FetchAsync_AppliesIncludeGlobs()
    {
        var source = CreateSource(BuildRepo());
        var config = new IngestionSourceConfig { RepositoryUrl = RepoUrl, IncludeGlobs = ["docs/**"] };

        var items = await Collect(source, config);

        // Structural nodes (root + host + group + repo) are always emitted; only files are glob-filtered.
        items.Select(i => i.Id).Should().BeEquivalentTo(
            "git-knowledge",
            "git-host:gitlab.example",
            "git-group:group",
            "git:my-repo",
            "git:my-repo:docs/guide",
            "git:my-repo:docs/adr/0001-foo",
            "git:my-repo:docs/adr/0002-bar");
    }

    [Fact]
    public async Task FetchAsync_BuildsRepoHierarchy()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        // Full chain: file -> repo -> group -> host -> git-knowledge.
        items.Single(i => i.Id == "git:my-repo:notes/plain")
            .NativeLinks.Select(l => l.TargetRef).Should().Contain("git:my-repo");
        items.Single(i => i.Id == "git:my-repo")
            .NativeLinks.Select(l => l.TargetRef).Should().Contain("git-group:group");
        items.Single(i => i.Id == "git-group:group")
            .NativeLinks.Select(l => l.TargetRef).Should().Contain("git-host:gitlab.example");
        items.Single(i => i.Id == "git-host:gitlab.example")
            .NativeLinks.Select(l => l.TargetRef).Should().Contain("git-knowledge");

        items.Single(i => i.Id == "git-knowledge").Domain.Should().Be("git-knowledge");
    }

    [Fact]
    public async Task FetchAsync_EmptyRepositoryUrl_YieldsNothing()
    {
        var source = CreateSource(BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig());

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_PassesReferenceToGitClient()
    {
        var git = new FakeGitClient("repo");
        var source = new GitMarkdownSource(git, BuildRepo());

        await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl, Reference = "release/1.0" });

        git.LastRequest!.Reference.Should().Be("release/1.0");
        git.LastRequest.RepositoryUrl.Should().Be(RepoUrl);
    }

    [Fact]
    public async Task FetchAsync_CleansUpWorkingCopyAfterScanning()
    {
        var git = new FakeGitClient("repo");
        var source = new GitMarkdownSource(git, BuildRepo());

        await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        git.CleanupCount.Should().Be(1);
    }

    [Fact]
    public async Task FetchAsync_IngestsCodeFilesAndSkipsDependencyAndLockArtifacts()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("repo/src/Service.cs", "public class Service { }");
        fs.AddFile("repo/README.md", "# Repo");
        fs.AddFile("repo/node_modules/pkg/index.js", "module.exports = {};");
        fs.AddFile("repo/package-lock.json", "{}");
        fs.AddFile("repo/app.min.js", "var x=1");
        fs.AddFile("repo/logo.png", "binary");
        var source = CreateSource(fs);

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });
        var ids = items.Select(i => i.Id).ToList();

        ids.Should().Contain("git:my-repo:src/Service.cs");                 // code file kept (extension preserved)
        ids.Should().Contain("git:my-repo:README");                        // markdown kept (.md stripped)
        ids.Should().NotContain(id => id.Contains("node_modules", StringComparison.Ordinal));
        ids.Should().NotContain("git:my-repo:package-lock.json");
        ids.Should().NotContain("git:my-repo:app.min.js");
        ids.Should().NotContain("git:my-repo:logo.png");

        var code = items.Single(i => i.Id == "git:my-repo:src/Service.cs");
        code.Body.Should().Contain("public class Service");
        code.NativeLinks.Select(l => l.TargetRef).Should().Contain("git:my-repo");
    }

    [Fact]
    public async Task FetchAsync_RecordsGitContributorsOnRepoNode()
    {
        var git = new FakeGitClient("repo")
        {
            Contributors = [new GitContributor("Alice", 42), new GitContributor("Bob", 7)],
        };
        var source = new GitMarkdownSource(git, BuildRepo());

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });

        var repo = items.Single(i => i.Id == "git:my-repo");
        repo.Body.Should().Contain("Alice (42)");
        repo.Body.Should().Contain("Bob (7)");
    }

    [Fact]
    public async Task FetchAsync_SkipsUnreadableFile_AndImportsTheRest()
    {
        var fs = new InMemoryFileSystem();
        fs.AddFile("repo/README.md", "# Repo");
        fs.AddUnreadableFile("repo/src/Broken.cs");   // enumerated, throws on read (dangling symlink)
        var source = CreateSource(fs);

        var items = await Collect(source, new IngestionSourceConfig { RepositoryUrl = RepoUrl });
        var ids = items.Select(i => i.Id).ToList();

        ids.Should().Contain("git:my-repo:README");            // readable file still imported
        ids.Should().NotContain("git:my-repo:src/Broken.cs");  // unreadable file skipped, scan not aborted
    }

    [Theory]
    [InlineData("src/Program.cs", true)]
    [InlineData("docs/guide.md", true)]
    [InlineData("config.yaml", true)]
    [InlineData("node_modules/pkg/a.js", false)]
    [InlineData("dist/bundle.js", false)]
    [InlineData("app.min.js", false)]
    [InlineData("yarn.lock", false)]
    [InlineData("logo.png", false)]
    [InlineData("LICENSE", false)]
    public void IsIngestibleFile_ClassifiesByExtensionAndLocation(string path, bool expected)
        => GitMarkdownSource.IsIngestibleFile(path).Should().Be(expected);

    [Fact]
    public void CombineRelative_ParentEscapingRoot_ReturnsNull()
    {
        GitMarkdownSource.CombineRelative(string.Empty, "../outside.md").Should().BeNull();
    }

    [Fact]
    public void CombineRelative_ResolvesParentSegments()
    {
        GitMarkdownSource.CombineRelative("docs/adr", "../guide.md").Should().Be("docs/guide.md");
    }
}
