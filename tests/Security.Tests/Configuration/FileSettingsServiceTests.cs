using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.Security.Tests.Configuration;

public sealed class FileSettingsServiceTests
{
    private const string SettingsPath = "data/settings.json";

    private static FileSettingsService CreateSut(out Dictionary<string, string> storage)
    {
        storage = BuildStorage(out var fsMock);
        return new FileSettingsService(fsMock.Object, NullLogger<FileSettingsService>.Instance);
    }

    private static Dictionary<string, string> BuildStorage(out Mock<IFileSystem> fsMock)
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var mock = new Mock<IFileSystem>();

        mock.Setup(fs => fs.FileExists(It.IsAny<string>()))
            .Returns<string>(path => store.ContainsKey(path));

        mock.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<string, CancellationToken, IFileSystem, string>((path, _) => store[path]);

        mock.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, content, _) => store[path] = content)
            .Returns(Task.CompletedTask);

        mock.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));

        fsMock = mock;
        return store;
    }

    [Fact]
    public void Current_BeforeAnyLoad_ReturnsDefaults()
    {
        var sut = CreateSut(out _);

        sut.Current.SchemaVersion.Should().Be(1);
        sut.Current.General.EnableIngestion.Should().BeNull();
    }

    [Fact]
    public async Task ReloadAsync_NoFile_ReturnsDefaults()
    {
        var sut = CreateSut(out _);

        var result = await sut.ReloadAsync();

        result.SchemaVersion.Should().Be(1);
        result.General.EnableIngestion.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ThenReloadAsync_RoundTrips()
    {
        var sut = CreateSut(out _);
        var settings = new EddaSettings { General = new GeneralSettings { EnableIngestion = true } };

        await sut.SaveAsync(settings);
        var reloaded = await sut.ReloadAsync();

        reloaded.General.EnableIngestion.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_UpdatesCurrent()
    {
        var sut = CreateSut(out _);
        var settings = new EddaSettings { General = new GeneralSettings { EnableIngestion = false } };

        await sut.SaveAsync(settings);

        sut.Current.General.EnableIngestion.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_PersistsToExpectedPath()
    {
        var sut = CreateSut(out var storage);

        await sut.SaveAsync(new EddaSettings());

        storage.Should().ContainKey(SettingsPath);
    }

    [Fact]
    public async Task SaveAsync_RaisesChangedEvent()
    {
        var sut = CreateSut(out _);
        var raised = false;
        sut.Changed += (_, _) => raised = true;

        await sut.SaveAsync(new EddaSettings());

        raised.Should().BeTrue();
    }

    [Fact]
    public async Task ReloadAsync_RaisesChangedEvent()
    {
        var sut = CreateSut(out _);
        var raised = false;
        sut.Changed += (_, _) => raised = true;

        await sut.ReloadAsync();

        raised.Should().BeTrue();
    }

    [Fact]
    public async Task ReloadAsync_AfterExternalSave_ReflectsPersistedValue()
    {
        var first = CreateSut(out var storage);
        await first.SaveAsync(new EddaSettings { General = new GeneralSettings { EnableIngestion = true } });

        // A second instance over the same backing storage must observe the persisted value.
        var second = new FileSettingsService(
            BuildFileSystemOver(storage),
            NullLogger<FileSettingsService>.Instance);

        var reloaded = await second.ReloadAsync();

        reloaded.General.EnableIngestion.Should().BeTrue();
    }

    [Fact]
    public async Task ReloadAsync_CorruptFile_ReturnsDefaults()
    {
        var sut = CreateSut(out var storage);
        storage[SettingsPath] = "{ this is not valid json ]";

        var result = await sut.ReloadAsync();

        result.SchemaVersion.Should().Be(1);
        result.General.EnableIngestion.Should().BeNull();
    }

    private static IFileSystem BuildFileSystemOver(Dictionary<string, string> store)
    {
        var mock = new Mock<IFileSystem>();
        mock.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns<string>(path => store.ContainsKey(path));
        mock.Setup(fs => fs.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync<string, CancellationToken, IFileSystem, string>((path, _) => store[path]);
        mock.Setup(fs => fs.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, content, _) => store[path] = content)
            .Returns(Task.CompletedTask);
        mock.Setup(fs => fs.EnsureDirectoryExists(It.IsAny<string>()));
        return mock.Object;
    }
}
