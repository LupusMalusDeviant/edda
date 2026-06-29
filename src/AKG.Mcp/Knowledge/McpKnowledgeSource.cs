using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Edda.AKG.Mcp.Client;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Mcp.Knowledge;

/// <summary>
/// Ingestion source that treats an external MCP server as a knowledge source (SourceKind <c>mcp</c>): it
/// calls a configured tool (e.g. a search/list tool) via <see cref="IExternalMcpClient"/> and maps each
/// returned text content block to an <see cref="IngestionItem"/> under a shared source node, so the result
/// forms a connected subgraph. The server URL and bearer token are supplied per source instance (see
/// ADR-0006). The mapping is intentionally generic — what the tool returns is server-defined.
/// </summary>
public sealed class McpKnowledgeSource : IIngestionSource
{
    /// <summary>Settings key: external MCP server URL.</summary>
    public const string ServerUrlKey = "serverUrl";

    /// <summary>Settings key: name of the tool to call.</summary>
    public const string ToolNameKey = "toolName";

    /// <summary>Settings key: JSON object of arguments passed to the tool.</summary>
    public const string ArgumentsKey = "argsJson";

    /// <summary>Settings key: human-readable source label / id prefix.</summary>
    public const string LabelKey = "label";

    /// <summary>Settings key: explicit domain for the produced items.</summary>
    public const string DomainKey = "domain";

    private readonly IExternalMcpClientFactory _factory;

    /// <summary>Initializes a new instance of the <see cref="McpKnowledgeSource"/> class.</summary>
    /// <param name="factory">Factory building the MCP client for the configured instance.</param>
    public McpKnowledgeSource(IExternalMcpClientFactory factory) => _factory = factory;

    /// <inheritdoc />
    public string SourceKind => "mcp";

    /// <inheritdoc />
    public async IAsyncEnumerable<IngestionItem> FetchAsync(
        IngestionSourceConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!config.Settings.TryGetValue(ServerUrlKey, out var serverUrl) || string.IsNullOrWhiteSpace(serverUrl))
            yield break;
        if (!config.Settings.TryGetValue(ToolNameKey, out var toolName) || string.IsNullOrWhiteSpace(toolName))
            yield break;

        var label = Setting(config, LabelKey) is { Length: > 0 } l ? l : toolName;
        var domain = Setting(config, DomainKey) is { Length: > 0 } d ? d : Slug(label);
        var arguments = ParseArguments(Setting(config, ArgumentsKey));

        var client = _factory.Create(serverUrl, config.Token);
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);

        var texts = result.Content
            .Select(content => content.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        foreach (var item in BuildItems(label, domain, texts))
            yield return item;
    }

    /// <summary>Builds the source node plus one item per text block, connected to the source node.</summary>
    /// <param name="label">Source label (also id prefix).</param>
    /// <param name="domain">Domain assigned to all produced items.</param>
    /// <param name="texts">The text content blocks returned by the tool.</param>
    /// <returns>The connected ingestion items.</returns>
    internal static IReadOnlyList<IngestionItem> BuildItems(string label, string domain, IReadOnlyList<string> texts)
    {
        var slug = Slug(label);
        var sourceId = $"mcp:{slug}";
        var items = new List<IngestionItem>
        {
            new()
            {
                Id = sourceId,
                Title = label,
                Body = $"MCP-Wissensquelle '{label}'.",
                SourceKind = "mcp",
                Domain = domain,
                Tags = ["mcp", slug],
            },
        };

        for (var i = 0; i < texts.Count; i++)
        {
            var text = texts[i].Trim();
            items.Add(new IngestionItem
            {
                Id = $"{sourceId}:{i}",
                Title = FirstLine(text) is { Length: > 0 } heading ? heading : $"{label} #{i + 1}",
                Body = text,
                SourceKind = "mcp",
                Domain = domain,
                Tags = ["mcp"],
                NativeLinks = [new IngestionLink { Kind = "related", TargetRef = sourceId }],
            });
        }

        return items;
    }

    /// <summary>Parses a JSON object of tool arguments; empty/blank input yields no arguments.</summary>
    /// <param name="argsJson">A JSON object, or null/blank.</param>
    /// <returns>The argument map (values kept as JSON elements).</returns>
    internal static IReadOnlyDictionary<string, object?> ParseArguments(string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(argsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>(StringComparer.Ordinal);

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            arguments[property.Name] = property.Value.Clone();

        return arguments;
    }

    private static string? Setting(IngestionSourceConfig config, string key)
        => config.Settings.TryGetValue(key, out var value) ? value : null;

    private static string? FirstLine(string text)
    {
        var newline = text.IndexOfAny(['\n', '\r']);
        var line = (newline >= 0 ? text[..newline] : text).Trim().TrimStart('#').Trim();
        return line.Length == 0 ? null : line.Length > 120 ? line[..120] : line;
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "mcp" : slug;
    }
}
