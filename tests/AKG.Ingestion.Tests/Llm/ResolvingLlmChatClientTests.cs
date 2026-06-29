using Edda.AKG.Ingestion.Llm;
using Edda.AKG.Ingestion.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Llm;

/// <summary>Unit tests for <see cref="ResolvingLlmChatClient"/> settings/credential resolution.</summary>
public sealed class ResolvingLlmChatClientTests
{
    private static (ResolvingLlmChatClient Sut, Mock<ILlmChatClientFactory> Factory) CreateSut(
        LlmEnrichmentSettings llm,
        IReadOnlyDictionary<string, string?>? credentials = null,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Current).Returns(new EddaSettings { LlmEnrichment = llm });

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

        var factory = new Mock<ILlmChatClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<LlmProviderConfig>())).Returns(new FakeLlmChatClient("result"));

        var sut = new ResolvingLlmChatClient(
            settings.Object, credStore.Object, factory.Object, identity.Object, configuration.Object);
        return (sut, factory);
    }

    [Fact]
    public void ProviderName_SettingsWinsOverEnv()
    {
        var (sut, _) = CreateSut(
            new LlmEnrichmentSettings { Provider = "anthropic" },
            env: new Dictionary<string, string?> { ["INGESTION_LLM_PROVIDER"] = "openai" });

        sut.ProviderName.Should().Be("anthropic");
    }

    [Fact]
    public void ProviderName_EnvFallback_WhenSettingsEmpty()
    {
        var (sut, _) = CreateSut(
            new LlmEnrichmentSettings(),
            env: new Dictionary<string, string?> { ["INGESTION_LLM_PROVIDER"] = "openai" });

        sut.ProviderName.Should().Be("openai");
    }

    [Fact]
    public void ProviderName_Default_WhenNothingConfigured()
    {
        var (sut, _) = CreateSut(new LlmEnrichmentSettings());

        sut.ProviderName.Should().Be("openrouter");
    }

    [Fact]
    public async Task CompleteAsync_ResolvesConfigFromSettingsAndCredentials_AndDelegates()
    {
        LlmProviderConfig? captured = null;
        var (sut, factory) = CreateSut(
            new LlmEnrichmentSettings { Provider = "anthropic", Model = "claude-x" },
            credentials: new Dictionary<string, string?> { ["local:llm:anthropic"] = "sk-key" });
        factory.Setup(f => f.Create(It.IsAny<LlmProviderConfig>()))
            .Callback<LlmProviderConfig>(c => captured = c)
            .Returns(new FakeLlmChatClient("result"));

        var result = await sut.CompleteAsync("sys", "user");

        result.Should().Be("result");
        captured.Should().NotBeNull();
        captured!.Provider.Should().Be("anthropic");
        captured.Model.Should().Be("claude-x");
        captured.ApiKey.Should().Be("sk-key");
    }

    [Fact]
    public async Task CompleteAsync_ApiKey_FallsBackToEnv_WhenNotInStore()
    {
        LlmProviderConfig? captured = null;
        var (sut, factory) = CreateSut(
            new LlmEnrichmentSettings { Provider = "openai" },
            env: new Dictionary<string, string?> { ["INGESTION_LLM_API_KEY"] = "env-key" });
        factory.Setup(f => f.Create(It.IsAny<LlmProviderConfig>()))
            .Callback<LlmProviderConfig>(c => captured = c)
            .Returns(new FakeLlmChatClient("ok"));

        await sut.CompleteAsync("sys", "user");

        captured!.ApiKey.Should().Be("env-key");
    }
}
