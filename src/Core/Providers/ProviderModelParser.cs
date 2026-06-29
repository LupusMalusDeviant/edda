using System.Text.Json;

namespace Edda.Core.Providers;

/// <summary>
/// Parses the model-listing responses of the supported provider APIs into a flat, de-duplicated, sorted
/// list of model identifiers. Pure and deterministic — the HTTP calls live in the probe implementation, so
/// this parsing logic is fully unit-testable. Malformed input yields an empty list rather than throwing.
/// </summary>
public static class ProviderModelParser
{
    /// <summary>Parses an Ollama <c>/api/tags</c> response — <c>{"models":[{"name":"bge-m3:latest"}, …]}</c>.</summary>
    /// <param name="json">The raw JSON response body.</param>
    /// <returns>The model names, de-duplicated and sorted.</returns>
    public static IReadOnlyList<string> ParseOllamaTags(string json)
        => ExtractArray(json, "models", "name");

    /// <summary>Parses an OpenAI-compatible <c>/v1/models</c> response — <c>{"data":[{"id":"gpt-4o"}, …]}</c>.</summary>
    /// <param name="json">The raw JSON response body.</param>
    /// <returns>The model ids, de-duplicated and sorted. Also covers Anthropic's <c>/v1/models</c>.</returns>
    public static IReadOnlyList<string> ParseOpenAiModels(string json)
        => ExtractArray(json, "data", "id");

    /// <summary>Parses a Google/Gemini <c>models</c> response — <c>{"models":[{"name":"models/gemini-…"}, …]}</c>.</summary>
    /// <param name="json">The raw JSON response body.</param>
    /// <returns>The model names with any <c>models/</c> prefix stripped, de-duplicated and sorted.</returns>
    public static IReadOnlyList<string> ParseGeminiModels(string json)
        => ExtractArray(json, "models", "name")
            .Select(n => n.StartsWith("models/", StringComparison.Ordinal) ? n["models/".Length..] : n)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> ExtractArray(string json, string arrayProperty, string itemProperty)
    {
        var result = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(arrayProperty, out var array)
                && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty(itemProperty, out var value)
                        && value.ValueKind == JsonValueKind.String)
                    {
                        var name = value.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            result.Add(name!);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Malformed response → no models (the probe still reports reachability separately).
        }

        return result.Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }
}
