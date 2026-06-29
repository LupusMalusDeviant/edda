using Edda.AKG.Ingestion.Enrichment;
using Edda.AKG.Ingestion.Llm;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Enrichment;

/// <summary>Unit tests for the runtime enable/disable gate of <see cref="ResolvingIngestionEnricher"/>.</summary>
public sealed class ResolvingIngestionEnricherTests
{
    private static IngestionItem SampleItem() => new()
    {
        Id = "git:repo:doc",
        Title = "Doc",
        Body = "content",
        SourceKind = "git",
    };

    private static (ResolvingIngestionEnricher Sut, Mock<ILlmChatClient> Chat) CreateSut(
        bool? enabled,
        string? enricherEnv = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current)
            .Returns(new EddaSettings { LlmEnrichment = new LlmEnrichmentSettings { Enabled = enabled } });

        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);
        configuration.Setup(c => c["INGESTION_ENRICHER"]).Returns(enricherEnv);

        var chat = new Mock<ILlmChatClient>();
        chat.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{ "summary": "s", "related": [] }""");

        var inner = new LlmIngestionEnricher(chat.Object, NullLogger<LlmIngestionEnricher>.Instance);
        var sut = new ResolvingIngestionEnricher(settings.Object, configuration.Object, inner);
        return (sut, chat);
    }

    [Fact]
    public async Task EnrichAsync_Enabled_DelegatesToLlmEnricher()
    {
        var (sut, chat) = CreateSut(enabled: true);

        await sut.EnrichAsync(SampleItem(), ["other"]);

        chat.Verify(
            c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnrichAsync_Disabled_ReturnsItemUnchanged_WithoutCallingLlm()
    {
        var (sut, chat) = CreateSut(enabled: false);
        var item = SampleItem();

        var result = await sut.EnrichAsync(item, ["other"]);

        result.Should().BeSameAs(item);
        chat.Verify(
            c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnrichAsync_NullEnabled_EnvLlm_Delegates()
    {
        var (sut, chat) = CreateSut(enabled: null, enricherEnv: "llm");

        await sut.EnrichAsync(SampleItem(), ["other"]);

        chat.Verify(
            c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnrichAsync_NullEnabled_NoEnv_ReturnsUnchanged()
    {
        var (sut, chat) = CreateSut(enabled: null);
        var item = SampleItem();

        var result = await sut.EnrichAsync(item, ["other"]);

        result.Should().BeSameAs(item);
        chat.Verify(
            c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
