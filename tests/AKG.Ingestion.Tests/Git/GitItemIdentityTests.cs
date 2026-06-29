using Edda.AKG.Ingestion.Git;

namespace Edda.AKG.Ingestion.Tests.Git;

/// <summary>Unit tests for <see cref="GitItemIdentity"/>.</summary>
public sealed class GitItemIdentityTests
{
    [Theory]
    [InlineData("https://gitlab.example/group/my-repo.git", "my-repo")]
    [InlineData("https://gitlab.example/group/my-repo", "my-repo")]
    [InlineData("git@gitlab.example:group/proj.git", "proj")]
    [InlineData("https://gitlab.example/group/sub/", "sub")]
    public void Slug_DerivesRepositoryName(string url, string expected)
    {
        GitItemIdentity.Slug(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Slug_BlankUrl_ReturnsFallback(string url)
    {
        GitItemIdentity.Slug(url).Should().Be("repo");
    }

    [Fact]
    public void ItemId_StripsExtensionAndNormalizesSeparators()
    {
        GitItemIdentity.ItemId("my-repo", "docs\\adr\\0001-foo.md")
            .Should().Be("git:my-repo:docs/adr/0001-foo");
    }

    [Fact]
    public void ItemId_StripsLeadingDotSlash()
    {
        GitItemIdentity.ItemId("r", "./README.md").Should().Be("git:r:README");
    }

    [Theory]
    [InlineData("https://git.example.com/acme/team/group/sub/service.git",
        "git.example.com", "acme/team/group/sub", "service")]
    [InlineData("https://gitlab.example/group/sub/my-repo", "gitlab.example", "group/sub", "my-repo")]
    [InlineData("git@gitlab.example:group/proj.git", "gitlab.example", "group", "proj")]
    public void Parse_RemoteUrl_ExtractsHostNamespaceAndRepo(string url, string host, string ns, string repo)
    {
        var (h, n, r) = GitItemIdentity.Parse(url);

        h.Should().Be(host);
        string.Join('/', n).Should().Be(ns);
        r.Should().Be(repo);
    }

    [Theory]
    [InlineData("C:/Users/me/Temp/service")]
    [InlineData("/var/repos/service")]
    public void Parse_LocalPath_HasNoHostOrNamespace(string path)
    {
        var (host, ns, repo) = GitItemIdentity.Parse(path);

        host.Should().BeNull();
        ns.Should().BeEmpty();
        repo.Should().Be("service");
    }

    [Fact]
    public void HostAndGroupIds_AreStable()
    {
        GitItemIdentity.HostId("git.example.com").Should().Be("git-host:git.example.com");
        GitItemIdentity.GroupId("grp/sub").Should().Be("git-group:grp/sub");
    }
}
