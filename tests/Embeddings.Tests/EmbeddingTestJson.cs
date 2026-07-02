namespace Edda.Agent.Providers.Tests.Embeddings;

/// <summary>Test helpers for building OpenAI-compatible embedding response JSON.</summary>
internal static class EmbeddingTestJson
{
    /// <summary>
    /// Builds a response body with <paramref name="count"/> items in REVERSED index order, where the
    /// embedding for item <c>i</c> is the single-element vector <c>[i]</c>. Reversing the order lets a
    /// test prove that the service reorders the response by its <c>index</c> field.
    /// </summary>
    /// <param name="count">Number of embedding items to emit.</param>
    /// <returns>An OpenAI-compatible <c>{"data":[...]}</c> response body.</returns>
    public static string ReversedBatch(int count)
    {
        var items = Enumerable.Range(0, count)
            .Reverse()
            .Select(i => $$"""{"index":{{i}},"embedding":[{{i}}]}""");
        return $$"""{"data":[{{string.Join(",", items)}}]}""";
    }
}
