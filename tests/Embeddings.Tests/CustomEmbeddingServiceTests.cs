using System.Net;
using System.Text.Json;
using Edda.Agent.Providers.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Edda.Agent.Providers.Tests.Embeddings;

/// <summary>
/// Unit tests for <see cref="CustomEmbeddingService"/> (any OpenAI-compatible endpoint) batch embedding:
/// a single request per batch and index-based reordering of the response.
/// </summary>
public class CustomEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedBatchAsync_TenTexts_IssuesSingleRequestAndPreservesOrder()
    {
        var requestCount = 0;
        var sut = BuildService(() => Interlocked.Increment(ref requestCount));

        var texts = Enumerable.Range(0, 10).Select(i => $"text-{i}").ToList();
        var result = await sut.EmbedBatchAsync(texts);

        requestCount.Should().Be(1);               // a single batch request for 10 texts
        result.Should().HaveCount(10);
        for (var i = 0; i < 10; i++)
            result[i][0].Should().BeApproximately(i, 0.0001f); // order preserved despite reversed response
    }

    [Fact]
    public async Task EmbedBatchAsync_AboveMaxBatchSize_SplitsIntoMultipleRequests()
    {
        var requestCount = 0;
        var sut = BuildService(() => Interlocked.Increment(ref requestCount));

        var count = CustomEmbeddingService.MaxBatchSize + 2; // → two batches
        var texts = Enumerable.Range(0, count).Select(i => $"text-{i}").ToList();
        var result = await sut.EmbedBatchAsync(texts);

        requestCount.Should().Be(2);
        result.Should().HaveCount(count);
    }

    [Fact]
    public async Task EmbedBatchAsync_NoTexts_MakesNoRequest()
    {
        var requestCount = 0;
        var sut = BuildService(() => Interlocked.Increment(ref requestCount));

        var result = await sut.EmbedBatchAsync([]);

        requestCount.Should().Be(0);
        result.Should().BeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CustomEmbeddingService BuildService(Action onRequest)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                onRequest();
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);
                var input = doc.RootElement.GetProperty("input");
                var n = input.ValueKind == JsonValueKind.Array ? input.GetArrayLength() : 1;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EmbeddingTestJson.ReversedBatch(n)),
                });
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object));

        return new CustomEmbeddingService(
            factory.Object,
            "http://localhost:1234/v1",
            "test-key",
            "custom-model",
            dimensions: 3,
            new Mock<ILogger<CustomEmbeddingService>>().Object);
    }
}
