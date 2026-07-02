using Edda.AKG.Ingestion.Llm;
using Edda.Core.Exceptions;

namespace Edda.AKG.Ingestion.Tests.TestUtilities;

/// <summary>
/// Fake <see cref="ILlmChatClient"/> for unit tests. Returns a canned response, throws, or plays back a
/// sequence of per-call behaviours (a response string or an exception to throw), and counts invocations.
/// </summary>
internal sealed class FakeLlmChatClient : ILlmChatClient
{
    private readonly Queue<Func<string>> _steps = new();
    private readonly Func<string> _fallback;

    /// <summary>A client that returns the same response on every call.</summary>
    public FakeLlmChatClient(string response) => _fallback = () => response;

    private FakeLlmChatClient(IEnumerable<Func<string>> steps, Func<string> fallback)
    {
        foreach (var step in steps)
            _steps.Enqueue(step);
        _fallback = fallback;
    }

    /// <summary>A client that throws a bare (non-transient) provider error on every call.</summary>
    public static FakeLlmChatClient Throwing() =>
        new([], () => throw new ProviderException("fake", "simulated provider failure"));

    /// <summary>Throws each of <paramref name="throwsFirst"/> in turn, then returns <paramref name="then"/>.</summary>
    public static FakeLlmChatClient ThrowsThenReturns(IEnumerable<Exception> throwsFirst, string then) =>
        new(throwsFirst.Select<Exception, Func<string>>(ex => () => throw ex), () => then);

    /// <summary>Returns each response in order; once exhausted, repeats the last one.</summary>
    public static FakeLlmChatClient Responses(params string[] responses)
    {
        var last = responses.Length > 0 ? responses[^1] : string.Empty;
        return new(responses.Select<string, Func<string>>(r => () => r), () => last);
    }

    /// <summary>Number of times <see cref="CompleteAsync"/> was invoked.</summary>
    public int CallCount { get; private set; }

    public string ProviderName => "fake";

    public string? LastUserPrompt { get; private set; }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastUserPrompt = userPrompt;
        var step = _steps.Count > 0 ? _steps.Dequeue() : _fallback;
        return Task.FromResult(step());
    }
}
