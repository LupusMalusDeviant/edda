using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public async Task CreateAsync_NewToken_PersistsVersionedFileWithPerTokenSaltedHash()
    {
        var sut = CreateSut(out var storage);

        var created = await sut.CreateAsync("t", ["a"], allowWrite: false);

        using var doc = JsonDocument.Parse(storage[Path]);
        doc.RootElement.GetProperty("Version").GetInt32().Should().Be(2, "the file carries a format version");

        var entry = doc.RootElement.GetProperty("Tokens")[0];
        entry.GetProperty("Salt").GetString().Should().NotBeNullOrEmpty("each token stores its own random salt");
        entry.GetProperty("Hash").GetString().Should()
            .NotBe(UnsaltedHash(created.Token), "the stored hash must be salted, not a plain SHA-256 of the token");

        (await sut.ResolveAsync(created.Token)).Should().NotBeNull("a freshly created salted token round-trips");
    }

    [Fact]
    public async Task ResolveAsync_LegacyUnsaltedEntry_StillResolves()
    {
        var sut = CreateSut(out var storage);
        const string token = "mcp_legacytoken";
        // Simulate a version-1 file: a bare array of entries hashed the old, unsalted way (no Salt, no Version).
        storage[Path] =
            $$"""
            [
              {
                "Id": "leg1", "Label": "old", "Hash": "{{UnsaltedHash(token)}}",
                "Tools": ["search_memory"], "AllowWrite": true, "CreatedAt": "2020-01-01T00:00:00+00:00"
              }
            ]
            """;

        var scopes = await sut.ResolveAsync(token);

        scopes.Should().NotBeNull("pre-salt tokens must keep working (backward compatibility)");
        scopes!.Id.Should().Be("leg1");
        scopes.Tools.Should().Equal("search_memory");
        scopes.AllowWrite.Should().BeTrue();
    }

    [Fact]
    public async Task Write_AfterLegacyLoad_MigratesFileToVersionedEnvelope()
    {
        var sut = CreateSut(out var storage);
        const string legacyToken = "mcp_legacytoken";
        storage[Path] =
            $$"""
            [
              {
                "Id": "leg1", "Label": "old", "Hash": "{{UnsaltedHash(legacyToken)}}",
                "Tools": ["a"], "AllowWrite": false, "CreatedAt": "2020-01-01T00:00:00+00:00"
              }
            ]
            """;

        // Any write (here: adding a new token) rewrites the whole file in the current versioned format.
        await sut.CreateAsync("new", ["b"], allowWrite: false);

        using var doc = JsonDocument.Parse(storage[Path]);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object, "the bare array is upgraded to an envelope");
        doc.RootElement.GetProperty("Version").GetInt32().Should().Be(2);

        // The legacy token still resolves after migration, and both entries survive.
        (await sut.ResolveAsync(legacyToken)).Should().NotBeNull("the migrated legacy entry stays valid");
        (await sut.ListAsync()).Should().HaveCount(2);
    }

    private static string UnsaltedHash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
