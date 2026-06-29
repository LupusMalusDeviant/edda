using Edda.AKG.Ingestion.Llm;
using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Tests.TestUtilities;

/// <summary>Fake <see cref="ILlmChatClient"/> returning a canned response, or throwing, for unit tests.</summary>
internal sealed class FakeLlmChatClient : ILlmChatClient
{
    private readonly string? _response;
    private readonly bool _throws;

    public FakeLlmChatClient(string response) => _response = response;

    private FakeLlmChatClient(bool throws) => _throws = throws;

    public static FakeLlmChatClient Throwing() => new(throws: true);

    public string ProviderName => "fake";

    public string? LastUserPrompt { get; private set; }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        LastUserPrompt = userPrompt;
        if (_throws)
            throw new ProviderException(ProviderName, "simulated provider failure");
        return Task.FromResult(_response ?? string.Empty);
    }
}
