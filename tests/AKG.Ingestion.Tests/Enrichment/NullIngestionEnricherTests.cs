using Edda.AKG.Ingestion.Enrichment;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Enrichment;

/// <summary>Unit tests for <see cref="NullIngestionEnricher"/>.</summary>
public sealed class NullIngestionEnricherTests
{
    private static IngestionItem SampleItem() => new()
    {
        Id = "git:repo:doc",
        Title = "Doc",
        Body = "Body",
        SourceKind = "git"
    };

    [Fact]
    public async Task EnrichAsync_WithKnownIds_ReturnsItemUnchanged()
    {
        var enricher = new NullIngestionEnricher();
        var item = SampleItem();

        var result = await enricher.EnrichAsync(item, ["git:repo:other"]);

        result.Should().BeSameAs(item);
    }

    [Fact]
    public async Task EnrichAsync_WithoutKnownIds_ReturnsItemUnchanged()
    {
        var enricher = new NullIngestionEnricher();
        var item = SampleItem();

        var result = await enricher.EnrichAsync(item, []);

        result.Should().BeSameAs(item);
    }
}
