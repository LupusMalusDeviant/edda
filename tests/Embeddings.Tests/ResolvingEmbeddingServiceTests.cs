using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Embeddings;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Edda.Embeddings.Tests;

/// <summary>Unit tests for <see cref="ResolvingEmbeddingService"/> settings/credential resolution.</summary>
public sealed class ResolvingEmbeddingServiceTests
{
    private static (ResolvingEmbeddingService Sut, Mock<IEmbeddingProviderFactory> Factory) CreateSut(
        EmbeddingSettings embedding,
        IReadOnlyDictionary<string, string?>? credentials = null,
        IReadOnlyDictionary<string, string?>? env = null,
        int dimensions = 1536,
        bool available = true)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(new EddaSettings { Embedding = embedding });

        var credStore = new Mock<ICredentialStore>();
        credStore.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        if (credentials is not null)
        {
            foreach (var kv in credentials)
            {
                credStore.Setup(c => c.RetrieveAsync(kv.Key, It.IsAny<CancellationToken>())).ReturnsAsync(kv.Value);
            }
        }

        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("local");

        var configuration = new Mock<IConfiguration>();
        configuration.Setup(c => c[It.IsAny<string>()]).Returns((string?)null);
        if (env is not null)
        {
            foreach (var kv in env)
            {
                configuration.Setup(c => c[kv.Key]).Returns(kv.Value);
            }
        }

        var built = new Mock<IEmbeddingService>();
        built.SetupGet(s => s.Dimensions).Returns(dimensions);
        built.SetupGet(s => s.IsAvailable).Returns(available);

        var factory = new Mock<IEmbeddingProviderFactory>();
        factory.Setup(f => f.Create(It.IsAny<EmbeddingProviderConfig>())).Returns(built.Object);

        var sut = new ResolvingEmbeddingService(
            settings.Object, credStore.Object, factory.Object, identity.Object, configuration.Object);
        return (sut, factory);
    }

    [Fact]
    public void Dimensions_ResolveFromBuiltProvider()
    {
        var (sut, _) = CreateSut(new EmbeddingSettings { Provider = "ollama" }, dimensions: 768);

        sut.Dimensions.Should().Be(768);
    }

    [Fact]
    public void IsAvailable_FalseWhenBuiltProviderUnavailable()
    {
        var (sut, _) = CreateSut(new EmbeddingSettings { Provider = "null" }, available: false);

        sut.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task EmbedAsync_ResolvesProviderFromSettings_KeyFromStore_AndDelegates()
    {
        EmbeddingProviderConfig? captured = null;
        var keyed = new Mock<IEmbeddingService>();
        keyed.Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 1f });
        var (sut, factory) = CreateSut(
            new EmbeddingSettings { Provider = "openai", Model = "text-embedding-3-large" },
            credentials: new Dictionary<string, string?> { ["local:embed:openai"] = "ek" });
        factory.Setup(f => f.Create(It.Is<EmbeddingProviderConfig>(c => c.ApiKey != null)))
            .Callback<EmbeddingProviderConfig>(c => captured = c)
            .Returns(keyed.Object);

        var result = await sut.EmbedAsync("hello");

        result.Should().Equal(new float[] { 1f });
        captured.Should().NotBeNull();
        captured!.Provider.Should().Be("openai");
        captured.Model.Should().Be("text-embedding-3-large");
        captured.ApiKey.Should().Be("ek");
    }

    [Fact]
    public async Task EmbedAsync_ApiKey_FallsBackToEnv_WhenNotInStore()
    {
        EmbeddingProviderConfig? captured = null;
        var keyed = new Mock<IEmbeddingService>();
        keyed.Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 1f });
        var (sut, factory) = CreateSut(
            new EmbeddingSettings { Provider = "openai" },
            env: new Dictionary<string, string?> { ["EMBEDDING_API_KEY"] = "env-ek" });
        factory.Setup(f => f.Create(It.Is<EmbeddingProviderConfig>(c => c.ApiKey != null)))
            .Callback<EmbeddingProviderConfig>(c => captured = c)
            .Returns(keyed.Object);

        await sut.EmbedAsync("hi");

        captured!.ApiKey.Should().Be("env-ek");
    }

    [Fact]
    public async Task EmbedAsync_Bedrock_ResolvesRegionAndAccessKeyId_FromSettingsAndStore()
    {
        EmbeddingProviderConfig? captured = null;
        var keyed = new Mock<IEmbeddingService>();
        keyed.Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 1f });
        var (sut, factory) = CreateSut(
            new EmbeddingSettings { Provider = "bedrock", Region = "eu-central-1" },
            credentials: new Dictionary<string, string?>
            {
                ["local:embed:bedrock"] = "secret",
                ["local:embed:bedrock:accesskey"] = "AKIA123",
            });
        factory.Setup(f => f.Create(It.Is<EmbeddingProviderConfig>(c => c.ApiKey != null)))
            .Callback<EmbeddingProviderConfig>(c => captured = c)
            .Returns(keyed.Object);

        await sut.EmbedAsync("hello");

        captured.Should().NotBeNull();
        captured!.Provider.Should().Be("bedrock");
        captured.Region.Should().Be("eu-central-1");
        captured.AccessKeyId.Should().Be("AKIA123");
        captured.ApiKey.Should().Be("secret");
    }

    [Fact]
    public async Task EmbedAsync_Bedrock_RegionAndAccessKeyId_FallBackToEnv()
    {
        EmbeddingProviderConfig? captured = null;
        var keyed = new Mock<IEmbeddingService>();
        keyed.Setup(s => s.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 1f });
        var (sut, factory) = CreateSut(
            new EmbeddingSettings { Provider = "bedrock" },
            env: new Dictionary<string, string?>
            {
                ["EMBEDDING_REGION"] = "us-west-2",
                ["EMBEDDING_ACCESS_KEY_ID"] = "env-akid",
            });
        factory.Setup(f => f.Create(It.IsAny<EmbeddingProviderConfig>()))
            .Callback<EmbeddingProviderConfig>(c => captured = c)
            .Returns(keyed.Object);

        await sut.EmbedAsync("hi");

        captured!.Region.Should().Be("us-west-2");
        captured.AccessKeyId.Should().Be("env-akid");
    }
}
