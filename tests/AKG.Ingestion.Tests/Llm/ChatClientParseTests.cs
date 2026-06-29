using Edda.AKG.Ingestion.Llm;

namespace Edda.AKG.Ingestion.Tests.Llm;

/// <summary>Unit tests for the chat-client response parsers.</summary>
public sealed class ChatClientParseTests
{
    [Fact]
    public void OpenRouter_ParseContent_ExtractsMessageContent()
    {
        const string json = """{ "choices": [ { "message": { "role": "assistant", "content": "hello" } } ] }""";

        OpenRouterChatClient.ParseContent(json).Should().Be("hello");
    }

    [Fact]
    public void OpenRouter_ParseContent_NoChoices_ReturnsEmpty()
    {
        OpenRouterChatClient.ParseContent("""{ "choices": [] }""").Should().BeEmpty();
    }

    [Fact]
    public void Ollama_ParseContent_ExtractsMessageContent()
    {
        const string json = """{ "message": { "role": "assistant", "content": "hi there" }, "done": true }""";

        OllamaChatClient.ParseContent(json).Should().Be("hi there");
    }

    [Fact]
    public void Ollama_ParseContent_NoMessage_ReturnsEmpty()
    {
        OllamaChatClient.ParseContent("""{ "done": true }""").Should().BeEmpty();
    }

    [Fact]
    public void Anthropic_ParseContent_ExtractsTextBlocks()
    {
        const string json = """{ "content": [ { "type": "text", "text": "summary text" } ], "stop_reason": "end_turn" }""";

        AnthropicChatClient.ParseContent(json).Should().Be("summary text");
    }

    [Fact]
    public void Anthropic_ParseContent_Refusal_ReturnsEmpty()
    {
        const string json = """{ "content": [ { "type": "text", "text": "x" } ], "stop_reason": "refusal" }""";

        AnthropicChatClient.ParseContent(json).Should().BeEmpty();
    }

    [Fact]
    public void Anthropic_ParseContent_NoContent_ReturnsEmpty()
    {
        AnthropicChatClient.ParseContent("""{ "stop_reason": "end_turn" }""").Should().BeEmpty();
    }

    [Fact]
    public void OpenAiCompatible_ParseContent_ExtractsMessageContent()
    {
        const string json = """{ "choices": [ { "message": { "role": "assistant", "content": "hello" } } ] }""";

        OpenAiCompatibleChatClient.ParseContent(json).Should().Be("hello");
    }

    [Fact]
    public void OpenAiCompatible_ParseContent_NoChoices_ReturnsEmpty()
    {
        OpenAiCompatibleChatClient.ParseContent("""{ "choices": [] }""").Should().BeEmpty();
    }

    [Fact]
    public void Gemini_ParseContent_ExtractsParts()
    {
        const string json = """{ "candidates": [ { "content": { "parts": [ { "text": "g1" }, { "text": "g2" } ] } } ] }""";

        GeminiChatClient.ParseContent(json).Should().Be("g1g2");
    }

    [Fact]
    public void Gemini_ParseContent_NoCandidates_ReturnsEmpty()
    {
        GeminiChatClient.ParseContent("""{ "candidates": [] }""").Should().BeEmpty();
    }
}
