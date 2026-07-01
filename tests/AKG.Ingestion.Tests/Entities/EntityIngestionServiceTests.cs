using Edda.AKG.Ingestion.Entities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Entities;

/// <summary>Unit tests for <see cref="EntityIngestionService"/> (extractor + store mocked).</summary>
public sealed class EntityIngestionServiceTests
{
    private static EntityIngestionService Service(Mock<IEntityExtractor> extractor, Mock<IEntityStore> store)
        => new(extractor.Object, store.Object, NullLogger<EntityIngestionService>.Instance);

    private static EntityExtractionResult OneEntity()
        => new() { Entities = [new ExtractedEntity { Name = "Neo4j" }] };

    [Fact]
    public async Task IngestTextAsync_BlankText_ReturnsEmpty_WithoutExtracting()
    {
        var extractor = new Mock<IEntityExtractor>();
        var store = new Mock<IEntityStore>();

        var result = await Service(extractor, store).IngestTextAsync("  ", null, "u", "manual");

        result.Should().BeSameAs(EntityIngestionResult.Empty);
        extractor.Verify(
            e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestTextAsync_ExtractsAndPersists()
    {
        var extractor = new Mock<IEntityExtractor>();
        extractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OneEntity());
        var store = new Mock<IEntityStore>();
        store
            .Setup(s => s.IngestAsync(It.IsAny<EntityExtractionResult>(), "u", "manual", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityIngestionResult { EntitiesIngested = 1 });

        var result = await Service(extractor, store).IngestTextAsync("Neo4j is a graph db", null, "u", "manual");

        result.EntitiesIngested.Should().Be(1);
        store.Verify(
            s => s.IngestAsync(It.IsAny<EntityExtractionResult>(), "u", "manual", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IngestTextAsync_NoEntitiesExtracted_ReturnsEmpty_WithoutPersisting()
    {
        var extractor = new Mock<IEntityExtractor>();
        extractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EntityExtractionResult.Empty);
        var store = new Mock<IEntityStore>();

        var result = await Service(extractor, store).IngestTextAsync("nothing useful", null, "u", "manual");

        result.Should().BeSameAs(EntityIngestionResult.Empty);
        store.Verify(
            s => s.IngestAsync(
                It.IsAny<EntityExtractionResult>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IngestTextAsync_StoreThrows_ReturnsEmpty()
    {
        var extractor = new Mock<IEntityExtractor>();
        extractor
            .Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OneEntity());
        var store = new Mock<IEntityStore>();
        store
            .Setup(s => s.IngestAsync(
                It.IsAny<EntityExtractionResult>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store down"));

        var result = await Service(extractor, store).IngestTextAsync("Neo4j", null, "u", "manual");

        result.Should().BeSameAs(EntityIngestionResult.Empty);
    }
}
