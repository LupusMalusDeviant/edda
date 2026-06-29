using System.Net;
using Edda.Agent.Providers.Embeddings;
using Edda.Core.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Moq.Protected;

namespace Edda.Agent.Providers.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="OllamaEmbeddingService"/> covering circuit-breaker behavior,
/// successful embedding calls, and error handling.
/// </summary>
public class OllamaEmbeddingServiceTests
{
    private const string BaseUrl = "http://ollama:11434";
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);

    // ── IsAvailable / Circuit-Breaker ─────────────────────────────────────────

    [Fact]
    public void IsAvailable_InitialState_ReturnsTrue()
    {
        var sut = BuildService(HttpStatusCode.OK, BuildEmbedJson());

        sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailable_AfterThresholdFailures_ReturnsFalse()
    {
        var sut = BuildService(HttpStatusCode.ServiceUnavailable, "error");

        for (var i = 0; i < OllamaEmbeddingService.FailureThreshold; i++)
        {
            await sut.Invoking(s => s.EmbedAsync("text")).Should().ThrowAsync<ProviderException>();
        }

        sut.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailable_BelowThreshold_StaysTrue()
    {
        var sut = BuildService(HttpStatusCode.ServiceUnavailable, "error");

        // One less than threshold
        for (var i = 0; i < OllamaEmbeddingService.FailureThreshold - 1; i++)
        {
            await sut.Invoking(s => s.EmbedAsync("text")).Should().ThrowAsync<ProviderException>();
        }

        sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailable_AfterCooldownExpires_ReturnsTrueAgain()
    {
        var sut = BuildService(HttpStatusCode.ServiceUnavailable, "error");

        for (var i = 0; i < OllamaEmbeddingService.FailureThreshold; i++)
        {
            await sut.Invoking(s => s.EmbedAsync("text")).Should().ThrowAsync<ProviderException>();
        }

        sut.IsAvailable.Should().BeFalse();

        // Advance past cooldown
        _timeProvider.Advance(TimeSpan.FromSeconds(OllamaEmbeddingService.CooldownSeconds + 1));

        sut.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailable_AfterSuccessFollowingFailures_ResetsToTrue()
    {
        var responses = new Queue<(HttpStatusCode, string)>();
        // Two failures (below threshold), then one success
        responses.Enqueue((HttpStatusCode.ServiceUnavailable, "error"));
        responses.Enqueue((HttpStatusCode.ServiceUnavailable, "error"));
        responses.Enqueue((HttpStatusCode.OK, BuildEmbedJson()));

        var sut = BuildServiceFromQueue(responses);

        await sut.Invoking(s => s.EmbedAsync("text")).Should().ThrowAsync<ProviderException>();
        await sut.Invoking(s => s.EmbedAsync("text")).Should().ThrowAsync<ProviderException>();
        await sut.EmbedAsync("text"); // success

        sut.IsAvailable.Should().BeTrue();
    }

    // ── EmbedAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmbedAsync_SuccessfulResponse_ReturnsEmbedding()
    {
        var sut = BuildService(HttpStatusCode.OK, BuildEmbedJson(1.0f, 2.0f, 3.0f));

        var result = await sut.EmbedAsync("hello world");

        result.Should().HaveCountGreaterThan(0);
        result[0].Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public async Task EmbedAsync_HttpError_ThrowsProviderException()
    {
        var sut = BuildService(HttpStatusCode.InternalServerError, "error");

        await sut.Invoking(s => s.EmbedAsync("text"))
            .Should().ThrowAsync<ProviderException>()
            .WithMessage("*Embedding request failed*");
    }

    [Fact]
    public async Task EmbedAsync_NetworkError_ThrowsProviderException()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var sut = BuildServiceWithHandler(handlerMock);

        await sut.Invoking(s => s.EmbedAsync("text"))
            .Should().ThrowAsync<ProviderException>();
    }

    // ── EmbedBatchAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task EmbedBatchAsync_MultipleTexts_ReturnsCorrectCount()
    {
        var sut = BuildService(HttpStatusCode.OK, BuildEmbedJson(0.1f, 0.2f));

        var result = await sut.EmbedBatchAsync(["text1", "text2", "text3"]);

        result.Should().HaveCount(3);
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dimensions_ReturnsDefaultDimensions()
    {
        var sut = BuildService(HttpStatusCode.OK, BuildEmbedJson());

        sut.Dimensions.Should().Be(OllamaEmbeddingService.DefaultDimensions);
    }

    [Fact]
    public void DefaultModel_IsNomicEmbedTextV2_With768Dimensions()
    {
        OllamaEmbeddingService.DefaultModel.Should().Be("nomic-embed-text-v2-moe");
        OllamaEmbeddingService.DefaultDimensions.Should().Be(768);
    }

    [Fact]
    public async Task EmbedAsync_DefaultModel_SendsNomicV2ModelNameInRequest()
    {
        string? capturedBody = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildEmbedJson()),
            });

        // BuildServiceWithHandler constructs the service with the default model.
        var sut = BuildServiceWithHandler(handlerMock);

        await sut.EmbedAsync("hello world");

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("nomic-embed-text-v2-moe");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private OllamaEmbeddingService BuildService(HttpStatusCode status, string body)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            // Use a factory so each concurrent call gets a fresh response/content instance.
            .ReturnsAsync(() => new HttpResponseMessage(status) { Content = new StringContent(body) });

        return BuildServiceWithHandler(handlerMock);
    }

    private OllamaEmbeddingService BuildServiceFromQueue(Queue<(HttpStatusCode, string)> responses)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var (code, body) = responses.Dequeue();
                return new HttpResponseMessage(code) { Content = new StringContent(body) };
            });

        return BuildServiceWithHandler(handlerMock);
    }

    private OllamaEmbeddingService BuildServiceWithHandler(Mock<HttpMessageHandler> handlerMock)
    {
        var factoryMock = new Mock<IHttpClientFactory>();
        // Return a fresh HttpClient per call so concurrent EmbedBatchAsync calls
        // don't share and accidentally dispose the same instance.
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handlerMock.Object));

        return new OllamaEmbeddingService(
            factoryMock.Object,
            _timeProvider,
            new Mock<ILogger<OllamaEmbeddingService>>().Object,
            BaseUrl);
    }

    private static string BuildEmbedJson(params float[] values)
    {
        if (values.Length == 0)
            values = [0.1f, 0.2f, 0.3f];

        var inner = string.Join(",", values.Select(v => v.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
        return $$"""{"embeddings":[[{{inner}}]]}""";
    }
}
