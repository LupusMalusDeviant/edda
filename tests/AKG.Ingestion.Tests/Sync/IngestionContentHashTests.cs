using Edda.AKG.Ingestion.Sync;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Tests.Sync;

/// <summary>Unit tests for <see cref="IngestionContentHash"/> (C5 content signature).</summary>
public sealed class IngestionContentHashTests
{
    private static IngestionItem Item(
        string title = "Title",
        string body = "Body",
        string? domain = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<IngestionLink>? links = null,
        string? chunkStyle = null)
        => new()
        {
            Id = "id-1",
            Title = title,
            Body = body,
            SourceKind = "git",
            Domain = domain,
            Tags = tags ?? [],
            NativeLinks = links ?? [],
            ChunkStyle = chunkStyle,
        };

    [Fact]
    public void Compute_SameItem_ReturnsSameHash()
        => IngestionContentHash.Compute(Item()).Should().Be(IngestionContentHash.Compute(Item()));

    [Fact]
    public void Compute_ChangedBody_ReturnsDifferentHash()
        => IngestionContentHash.Compute(Item(body: "A")).Should().NotBe(IngestionContentHash.Compute(Item(body: "B")));

    [Fact]
    public void Compute_ChangedTitle_ReturnsDifferentHash()
        => IngestionContentHash.Compute(Item(title: "X")).Should().NotBe(IngestionContentHash.Compute(Item(title: "Y")));

    [Fact]
    public void Compute_ChangedTags_ReturnsDifferentHash()
        => IngestionContentHash.Compute(Item(tags: ["a"])).Should().NotBe(IngestionContentHash.Compute(Item(tags: ["b"])));

    [Fact]
    public void Compute_ChangedLinks_ReturnsDifferentHash()
    {
        var a = Item(links: [new IngestionLink { Kind = "related", TargetRef = "x" }]);
        var b = Item(links: [new IngestionLink { Kind = "related", TargetRef = "y" }]);
        IngestionContentHash.Compute(a).Should().NotBe(IngestionContentHash.Compute(b));
    }

    [Fact]
    public void Compute_ReturnsLowercaseHex64()
        => IngestionContentHash.Compute(Item()).Should().MatchRegex("^[0-9a-f]{64}$");
}
