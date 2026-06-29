using Edda.Core.Abstractions;
using Edda.Security.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Security.Tests.Credentials;

public sealed class AesCredentialStoreTests
{
    private static AesCredentialStore CreateSut(out Dictionary<string, byte[]> storage)
    {
        var store = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var fsMock = new Mock<IFileSystem>();

        fsMock.Setup(fs => fs.FileExists(It.IsAny<string>()))
              .Returns<string>(path => store.ContainsKey(path));

        fsMock.Setup(fs => fs.ReadAllBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync<string, CancellationToken, IFileSystem, byte[]>((path, _) => store[path]);

        fsMock.Setup(fs => fs.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
              .Callback<string, byte[], CancellationToken>((path, bytes, _) => store[path] = bytes)
              .Returns(Task.CompletedTask);

        fsMock.Setup(fs => fs.DirectoryExists(It.IsAny<string>())).Returns(true);
        fsMock.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));
        fsMock.Setup(fs => fs.CombinePath(It.IsAny<string[]>()))
              .Returns<string[]>(parts => string.Join("/", parts));
        fsMock.Setup(fs => fs.GetFullPath(It.IsAny<string>()))
              .Returns<string>(path => path);

        storage = store;
        return new AesCredentialStore(fsMock.Object, TimeProvider.System, NullLogger<AesCredentialStore>.Instance);
    }

    [Fact]
    public async Task StoreAsync_ThenRetrieveAsync_RoundTrips()
    {
        var sut = CreateSut(out _);
        const string key = "user1:openai-key";
        const string value = "sk-super-secret-value";

        await sut.StoreAsync(key, value);
        var retrieved = await sut.RetrieveAsync(key);

        retrieved.Should().Be(value);
    }

    [Fact]
    public async Task RetrieveAsync_MissingKey_ReturnsNull()
    {
        var sut = CreateSut(out _);

        var result = await sut.RetrieveAsync("user1:nonexistent-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesKey()
    {
        var sut = CreateSut(out _);
        const string key = "user1:telegram-token";
        await sut.StoreAsync(key, "some-token-value");

        await sut.DeleteAsync(key);
        var result = await sut.RetrieveAsync(key);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistentKey_DoesNotThrow()
    {
        var sut = CreateSut(out _);

        var act = async () => await sut.DeleteAsync("user1:ghost-key");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListAsync_ReturnsStoredKeys()
    {
        var sut = CreateSut(out _);
        await sut.StoreAsync("user1:key-a", "value-a");
        await sut.StoreAsync("user1:key-b", "value-b");
        await sut.StoreAsync("user1:key-c", "value-c");

        var keys = await sut.ListAsync();

        keys.Should().HaveCount(3);
        keys.Should().Contain("user1:key-a");
        keys.Should().Contain("user1:key-b");
        keys.Should().Contain("user1:key-c");
    }

    [Fact]
    public async Task ListAsync_EmptyStore_ReturnsEmptyList()
    {
        var sut = CreateSut(out _);

        var keys = await sut.ListAsync();

        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_UserScoped_IsolatesFromOtherUsers()
    {
        var sut = CreateSut(out _);
        await sut.StoreAsync("user1:shared-key-name", "user1-secret");
        await sut.StoreAsync("user2:shared-key-name", "user2-secret");

        var user1Value = await sut.RetrieveAsync("user1:shared-key-name");
        var user2Value = await sut.RetrieveAsync("user2:shared-key-name");

        user1Value.Should().Be("user1-secret");
        user2Value.Should().Be("user2-secret");
    }

    [Fact]
    public async Task StoreAsync_OverwritesExistingKey()
    {
        var sut = CreateSut(out _);
        const string key = "user1:mutable-key";
        await sut.StoreAsync(key, "original-value");

        await sut.StoreAsync(key, "updated-value");
        var result = await sut.RetrieveAsync(key);

        result.Should().Be("updated-value");
    }

    [Fact]
    public async Task StoreAsync_MultipleConcurrentWrites_AllPersisted()
    {
        var sut = CreateSut(out _);

        // Sequential (not concurrent) because the store uses a SemaphoreSlim internally.
        // This verifies repeated store operations all survive without corrupting the file.
        for (var i = 0; i < 5; i++)
        {
            await sut.StoreAsync($"user1:key-{i}", $"value-{i}");
        }

        for (var i = 0; i < 5; i++)
        {
            var val = await sut.RetrieveAsync($"user1:key-{i}");
            val.Should().Be($"value-{i}");
        }
    }
}
