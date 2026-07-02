using Edda.AKG.Ingestion.Sync;
using Edda.AKG.Ingestion.Tests.TestUtilities;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Sync;

/// <summary>Unit tests for <see cref="FileSyncStateStore"/> (C5 manifest persistence over <c>IFileSystem</c>).</summary>
public sealed class FileSyncStateStoreTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly FileSyncStateStore _sut;

    public FileSyncStateStoreTests() => _sut = new FileSyncStateStore(_fs);

    private static IngestionSyncState State(params (string Id, string Hash)[] entries)
        => new() { ItemHashes = entries.ToDictionary(e => e.Id, e => e.Hash, StringComparer.Ordinal) };

    [Fact]
    public async Task SaveThenLoad_RoundTripsItemHashes()
    {
        await _sut.SaveAsync("git|repo", State(("a", "h1"), ("b", "h2")));

        var loaded = await _sut.LoadAsync("git|repo");

        loaded.ItemHashes.Should().HaveCount(2);
        loaded.ItemHashes["a"].Should().Be("h1");
        loaded.ItemHashes["b"].Should().Be("h2");
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmptyState()
        => (await _sut.LoadAsync("git|unknown")).ItemHashes.Should().BeEmpty();

    [Fact]
    public async Task Load_CorruptJson_ReturnsEmptyState()
    {
        await _sut.SaveAsync("git|repo", State(("a", "h")));
        var file = _fs.EnumerateFiles(FileSyncStateStore.DefaultRoot, "*", recursive: true).Single();
        await _fs.WriteAllTextAsync(file, "{ this is not valid json ");

        (await _sut.LoadAsync("git|repo")).ItemHashes.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_DifferentInstanceKeys_WriteSeparateFiles()
    {
        await _sut.SaveAsync("k1", State(("a", "1")));
        await _sut.SaveAsync("k2", State(("b", "2")));

        var s1 = await _sut.LoadAsync("k1");
        s1.ItemHashes.Should().ContainKey("a");
        s1.ItemHashes.Should().NotContainKey("b");
        (await _sut.LoadAsync("k2")).ItemHashes.Should().ContainKey("b");
    }
}
