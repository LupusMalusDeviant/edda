using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Mcp;
using Moq;

namespace Edda.Security.Tests.Mcp;

/// <summary>Unit tests for <see cref="FileMcpTokenStore"/>: create/resolve/revoke and hash-only persistence.</summary>
public sealed class FileMcpTokenStoreTests
{
    private const string Path = "data/mcp-tokens.json";

    private static FileMcpTokenStore CreateSut(out Dictionary<string, string> storage)
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var mock = new Mock<IFileSystem>();
        mock.Setup(fs => fs.GetFullPath(It.IsAny<string>())).Returns<string>(p => p);
        mock.Setup(fs => fs.CombinePath(It.IsAny<string[]>())).Returns<string[]>(parts => string.Join("/", parts));
        mock.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns<string>(p => store.ContainsKey(p));
        mock.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<string, CancellationToken, IFileSystem, string>((p, _) => store[p]);
        mock.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((p, c, _) => store[p] = c)
            .Returns(Task.CompletedTask);
        mock.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));
        storage = store;
        return new FileMcpTokenStore(mock.Object, TimeProvider.System);
    }

    [Fact]
    public async Task CreateAsync_ThenResolve_ReturnsScopes()
    {
        var sut = CreateSut(out _);

        var created = await sut.CreateAsync("test", ["search_memory"], allowWrite: false);

        created.Token.Should().StartWith("mcp_");
        var scopes = await sut.ResolveAsync(created.Token);
        scopes.Should().NotBeNull();
        scopes!.Tools.Should().Equal("search_memory");
        scopes.AllowWrite.Should().BeFalse();
        scopes.Id.Should().Be(created.Info.Id);
    }

    [Fact]
    public async Task ResolveAsync_UnknownOrEmpty_ReturnsNull()
    {
        var sut = CreateSut(out _);
        await sut.CreateAsync("t", ["a"], false);

        (await sut.ResolveAsync("mcp_wrong")).Should().BeNull();
        (await sut.ResolveAsync("")).Should().BeNull();
        (await sut.ResolveAsync(null)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RevokesToken()
    {
        var sut = CreateSut(out _);
        var created = await sut.CreateAsync("t", ["a"], false);

        (await sut.DeleteAsync(created.Info.Id)).Should().BeTrue();
        (await sut.ResolveAsync(created.Token)).Should().BeNull();
        (await sut.ListAsync()).Should().BeEmpty();
        (await sut.DeleteAsync("does-not-exist")).Should().BeFalse();
    }

    [Fact]
    public async Task PersistedFile_StoresHashNotPlaintext()
    {
        var sut = CreateSut(out var storage);

        var created = await sut.CreateAsync("t", ["a"], allowWrite: true);

        storage.Should().ContainKey(Path);
        storage[Path].Should().NotContain(created.Token, "the plaintext token must never be persisted");
    }

    [Fact]
    public async Task ListAsync_ReturnsMetadata()
    {
        var sut = CreateSut(out _);
        await sut.CreateAsync("Claude", ["list_memory"], allowWrite: true);

        var list = await sut.ListAsync();

        list.Should().ContainSingle();
        list[0].Label.Should().Be("Claude");
        list[0].AllowWrite.Should().BeTrue();
        list[0].Tools.Should().Equal("list_memory");
    }

    [Fact]
    public async Task CreateAsync_FiltersBlankToolsAndDeduplicates()
    {
        var sut = CreateSut(out _);

        var created = await sut.CreateAsync("t", ["a", "a", "", "  ", "b"], false);

        created.Info.Tools.Should().Equal("a", "b");
    }
}
