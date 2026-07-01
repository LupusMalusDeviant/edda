using System.Text;
using System.Text.Json;
using Edda.AKG.Ingestion.Llm;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Edda.Core.Models;
using Edda.Security.OutputFilter;
using Edda.Security.Sanitization;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Ingestion.Entities;

/// <summary>
/// LLM-backed <see cref="IEntityExtractor"/> (LightRAG-style, M2 / ADR-0010): extracts typed entities and
/// their relations from unstructured text. Best-effort — any LLM or parse failure returns
/// <see cref="EntityExtractionResult.Empty"/> so callers are never broken. Source text is redacted and
/// sanitized before it reaches the model (PRD-0001 FR-07). Relations that reference an entity not in the
/// extracted set are dropped to keep the graph consistent (never invents nodes).
/// </summary>
public sealed class LlmEntityExtractor : IEntityExtractor
{
    private const int MaxTextChars = 8000;

    private const string SystemPrompt =
        "You extract a knowledge graph from a document. Identify the key entities and the relations " +
        "between them. Respond with ONLY a JSON object of the form {\"entities\":[{\"name\":string," +
        "\"type\":string,\"description\":string}],\"relations\":[{\"source\":string,\"target\":string," +
        "\"description\":string,\"keywords\":[string]}]}. \"type\" is a coarse category (e.g. person, " +
        "organization, technology, concept, location, event). Every relation's \"source\" and \"target\" " +
        "MUST exactly match a listed entity \"name\". Extract only entities actually present in the text; " +
        "never invent facts. If nothing is extractable, use empty arrays. Output JSON only.";

    private readonly ILlmChatClient _chat;
    private readonly IInputSanitizer _sanitizer;
    private readonly ISecretRedactor _redactor;
    private readonly ILogger<LlmEntityExtractor> _logger;

    /// <summary>Initializes a new instance of the <see cref="LlmEntityExtractor"/> class.</summary>
    /// <param name="chat">The chat client used for completions.</param>
    /// <param name="sanitizer">Neutralizes prompt-injection patterns in source text before it reaches the model.</param>
    /// <param name="redactor">Redacts secrets from source text before it reaches the model.</param>
    /// <param name="logger">Logger for best-effort diagnostics.</param>
    public LlmEntityExtractor(
        ILlmChatClient chat,
        IInputSanitizer sanitizer,
        ISecretRedactor redactor,
        ILogger<LlmEntityExtractor> logger)
    {
        _chat = chat;
        _sanitizer = sanitizer;
        _redactor = redactor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EntityExtractionResult> ExtractAsync(
        string text,
        string? domainHint = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return EntityExtractionResult.Empty;

        string response;
        try
        {
            response = await _chat
                .CompleteAsync(SystemPrompt, BuildUserPrompt(text, domainHint), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning("Entity extraction skipped — LLM unavailable ({Message}) | {Component}", ex.Message, "AKG");
            return EntityExtractionResult.Empty;
        }

        if (!TryParse(response, out var parsed))
        {
            _logger.LogWarning("Entity extraction skipped — unparseable LLM response | {Component}", "AKG");
            return EntityExtractionResult.Empty;
        }

        var names = parsed.Entities.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var consistent = parsed.Relations
            .Where(r => names.Contains(r.Source) && names.Contains(r.Target))
            .ToList();

        return parsed with { Relations = consistent };
    }

    private string BuildUserPrompt(string text, string? domainHint)
    {
        var truncated = text.Length > MaxTextChars ? text[..MaxTextChars] : text;
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(domainHint))
            sb.Append("Domain hint: ").Append(Clean(domainHint)).Append('\n').Append('\n');
        sb.Append("Text:\n").Append(Clean(truncated));
        return sb.ToString();
    }

    /// <summary>
    /// Redacts secrets and neutralizes prompt-injection patterns in source text before it is embedded into
    /// a prompt and sent to the language model (PRD-0001 FR-07). Secrets are redacted first, then injection
    /// markers are filtered from the result.
    /// </summary>
    /// <param name="raw">The raw source text.</param>
    /// <returns>The redacted and sanitized text safe to include in a prompt.</returns>
    private string Clean(string raw) => _sanitizer.Sanitize(_redactor.Redact(raw)).Text;

    /// <summary>
    /// Extracts entities and relations from the model response, tolerating surrounding prose or code
    /// fences. Internal for unit testing. Returns false when no JSON object can be parsed.
    /// </summary>
    /// <param name="response">The raw model response.</param>
    /// <param name="result">The parsed extraction result, or <see cref="EntityExtractionResult.Empty"/>.</param>
    /// <returns>True when a JSON object was parsed; otherwise false.</returns>
    internal static bool TryParse(string response, out EntityExtractionResult result)
    {
        result = EntityExtractionResult.Empty;

        if (string.IsNullOrWhiteSpace(response))
            return false;

        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start)
            return false;

        try
        {
            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            result = new EntityExtractionResult
            {
                Entities = ParseEntities(root),
                Relations = ParseRelations(root),
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<ExtractedEntity> ParseEntities(JsonElement root)
    {
        var entities = new List<ExtractedEntity>();
        if (!root.TryGetProperty("entities", out var array) || array.ValueKind != JsonValueKind.Array)
            return entities;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            var name = GetString(element, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var type = GetString(element, "type");
            entities.Add(new ExtractedEntity
            {
                Name = name.Trim(),
                Type = string.IsNullOrWhiteSpace(type) ? "concept" : type.Trim(),
                Description = GetString(element, "description")?.Trim() ?? string.Empty,
            });
        }

        return entities;
    }

    private static IReadOnlyList<ExtractedRelation> ParseRelations(JsonElement root)
    {
        var relations = new List<ExtractedRelation>();
        if (!root.TryGetProperty("relations", out var array) || array.ValueKind != JsonValueKind.Array)
            return relations;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            var source = GetString(element, "source");
            var target = GetString(element, "target");
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                continue;

            relations.Add(new ExtractedRelation
            {
                Source = source.Trim(),
                Target = target.Trim(),
                Description = GetString(element, "description")?.Trim() ?? string.Empty,
                Keywords = ParseKeywords(element),
            });
        }

        return relations;
    }

    private static IReadOnlyList<string> ParseKeywords(JsonElement relation)
    {
        var keywords = new List<string>();
        if (!relation.TryGetProperty("keywords", out var array) || array.ValueKind != JsonValueKind.Array)
            return keywords;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                continue;

            var keyword = element.GetString();
            if (!string.IsNullOrWhiteSpace(keyword))
                keywords.Add(keyword.Trim());
        }

        return keywords;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
