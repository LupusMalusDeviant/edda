using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Sources;

/// <summary>Pagination strategy for <see cref="HttpApiSource"/>.</summary>
internal enum HttpApiPageMode
{
    /// <summary>Single request, no pagination.</summary>
    None,

    /// <summary>1-based page numbers (<c>page=1,2,3…</c>).</summary>
    Page,

    /// <summary>Zero-based item offset (<c>startAt=0,50,100…</c>).</summary>
    Offset,
}

/// <summary>Resolved configuration for one <see cref="HttpApiSource"/> run.</summary>
internal sealed record HttpApiSourceOptions
{
    /// <summary>Base URL of the API (e.g. <c>https://api.example.com</c>).</summary>
    public required string BaseUrl { get; init; }

    /// <summary>List path (and any query) appended to <see cref="BaseUrl"/>.</summary>
    public required string ListPath { get; init; }

    /// <summary>Optional auth header name (e.g. <c>Authorization</c>).</summary>
    public string? AuthHeader { get; init; }

    /// <summary>Resolved auth header value (template already filled with the token).</summary>
    public string? AuthValue { get; init; }

    /// <summary>Dotted path to the items array in the response; empty = the root is the array.</summary>
    public string? ItemsPath { get; init; }

    /// <summary>Dotted path within each item to its id.</summary>
    public string? IdField { get; init; }

    /// <summary>Dotted path within each item to its title.</summary>
    public string? TitleField { get; init; }

    /// <summary>Dotted path within each item to its body.</summary>
    public string? BodyField { get; init; }

    /// <summary>Dotted path within each item to its canonical URL.</summary>
    public string? UrlField { get; init; }

    /// <summary>Provenance label stamped on produced items (also used as their id prefix).</summary>
    public string SourceLabel { get; init; } = "custom-http";

    /// <summary>Optional explicit domain assigned to produced items.</summary>
    public string? Domain { get; init; }

    /// <summary>Pagination strategy.</summary>
    public HttpApiPageMode PageMode { get; init; } = HttpApiPageMode.None;

    /// <summary>Query parameter carrying the page number / offset.</summary>
    public string? PageParam { get; init; }

    /// <summary>Query parameter carrying the page size.</summary>
    public string? PageSizeParam { get; init; }

    /// <summary>Page size requested per call.</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Hard cap on the number of pages fetched.</summary>
    public int MaxPages { get; init; } = 20;
}

/// <summary>
/// Generic ingestion source for any JSON HTTP/REST API. A declarative mapping (base URL, list path,
/// auth header, the JSON path to the items array and to each item's id/title/body/url, plus optional
/// pagination) turns API responses into <see cref="IngestionItem"/>s — so new source types such as
/// Jira or Awork are configuration, not code (see ADR-0006). The access token is supplied server-side
/// via <see cref="IngestionSourceConfig.Token"/>; the public ingest endpoint never accepts it.
/// </summary>
public sealed class HttpApiSource : IIngestionSource
{
    /// <summary>Settings key: base URL.</summary>
    public const string BaseUrlKey = "baseUrl";

    /// <summary>Settings key: list path (and query).</summary>
    public const string ListPathKey = "listPath";

    /// <summary>Settings key: auth header name.</summary>
    public const string AuthHeaderKey = "authHeader";

    /// <summary>Settings key: auth value template (<c>{token}</c> is replaced by the stored token).</summary>
    public const string AuthTemplateKey = "authValueTemplate";

    /// <summary>Settings key: dotted path to the items array.</summary>
    public const string ItemsPathKey = "itemsPath";

    /// <summary>Settings key: dotted path to each item's id.</summary>
    public const string IdFieldKey = "idField";

    /// <summary>Settings key: dotted path to each item's title.</summary>
    public const string TitleFieldKey = "titleField";

    /// <summary>Settings key: dotted path to each item's body.</summary>
    public const string BodyFieldKey = "bodyField";

    /// <summary>Settings key: dotted path to each item's URL.</summary>
    public const string UrlFieldKey = "urlField";

    /// <summary>Settings key: provenance label / id prefix.</summary>
    public const string SourceLabelKey = "sourceLabel";

    /// <summary>Settings key: explicit domain.</summary>
    public const string DomainKey = "domain";

    /// <summary>Settings key: pagination mode (<c>none</c> | <c>page</c> | <c>offset</c>).</summary>
    public const string PageModeKey = "pageMode";

    /// <summary>Settings key: page/offset query parameter name.</summary>
    public const string PageParamKey = "pageParam";

    /// <summary>Settings key: page-size query parameter name.</summary>
    public const string PageSizeParamKey = "pageSizeParam";

    /// <summary>Settings key: page size.</summary>
    public const string PageSizeKey = "pageSize";

    /// <summary>Settings key: maximum pages.</summary>
    public const string MaxPagesKey = "maxPages";

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Initializes a new instance of the <see cref="HttpApiSource"/> class.</summary>
    /// <param name="httpClientFactory">Factory supplying pooled HTTP clients.</param>
    public HttpApiSource(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    /// <inheritdoc />
    public string SourceKind => "custom-http";

    /// <inheritdoc />
    public async IAsyncEnumerable<IngestionItem> FetchAsync(
        IngestionSourceConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var options = ParseOptions(config);
        if (options is null)
            yield break;

        var http = _httpClientFactory.CreateClient(nameof(HttpApiSource));
        var cursor = options.PageMode == HttpApiPageMode.Offset ? 0 : 1;

        for (var pages = 0; pages < options.MaxPages; pages++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUrl(options, cursor));
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (!string.IsNullOrWhiteSpace(options.AuthHeader) && !string.IsNullOrEmpty(options.AuthValue))
                request.Headers.TryAddWithoutValidation(options.AuthHeader, options.AuthValue);

            string json;
            using (var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            var items = MapItems(json, options);
            foreach (var item in items)
                yield return item;

            if (options.PageMode == HttpApiPageMode.None || items.Count < options.PageSize)
                break;

            cursor += options.PageMode == HttpApiPageMode.Offset ? options.PageSize : 1;
        }
    }

    /// <summary>Resolves a configuration into options, or null when the required fields are missing.</summary>
    /// <param name="config">The source configuration.</param>
    /// <returns>The parsed options, or null.</returns>
    internal static HttpApiSourceOptions? ParseOptions(IngestionSourceConfig config)
    {
        var settings = config.Settings;
        var baseUrl = Setting(settings, BaseUrlKey);
        var listPath = Setting(settings, ListPathKey);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(listPath))
            return null;

        var authHeader = Setting(settings, AuthHeaderKey);
        string? authValue = null;
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            var template = Setting(settings, AuthTemplateKey);
            template = string.IsNullOrEmpty(template) ? "{token}" : template;
            authValue = template.Replace("{token}", config.Token ?? string.Empty, StringComparison.Ordinal);
        }

        return new HttpApiSourceOptions
        {
            BaseUrl = baseUrl!,
            ListPath = listPath!,
            AuthHeader = authHeader,
            AuthValue = authValue,
            ItemsPath = Setting(settings, ItemsPathKey),
            IdField = Setting(settings, IdFieldKey),
            TitleField = Setting(settings, TitleFieldKey),
            BodyField = Setting(settings, BodyFieldKey),
            UrlField = Setting(settings, UrlFieldKey),
            SourceLabel = Setting(settings, SourceLabelKey) is { Length: > 0 } label ? label : "custom-http",
            Domain = Setting(settings, DomainKey),
            PageMode = ParseMode(Setting(settings, PageModeKey)),
            PageParam = Setting(settings, PageParamKey),
            PageSizeParam = Setting(settings, PageSizeParamKey),
            PageSize = Clamp(ParseInt(Setting(settings, PageSizeKey), 50), 1, 500),
            MaxPages = Clamp(ParseInt(Setting(settings, MaxPagesKey), 20), 1, 200),
        };
    }

    /// <summary>Builds the request URL for the given page cursor.</summary>
    /// <param name="options">The resolved options.</param>
    /// <param name="cursor">Page number (1-based) or item offset depending on the page mode.</param>
    /// <returns>The fully composed request URL.</returns>
    internal static string BuildUrl(HttpApiSourceOptions options, int cursor)
    {
        var url = options.BaseUrl.TrimEnd('/') + "/" + options.ListPath.TrimStart('/');
        if (options.PageMode == HttpApiPageMode.None)
            return url;

        var builder = new StringBuilder(url);
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        if (!string.IsNullOrWhiteSpace(options.PageParam))
        {
            builder.Append(separator).Append(options.PageParam).Append('=').Append(cursor);
            separator = "&";
        }

        if (!string.IsNullOrWhiteSpace(options.PageSizeParam))
            builder.Append(separator).Append(options.PageSizeParam).Append('=').Append(options.PageSize);

        return builder.ToString();
    }

    /// <summary>Maps a JSON response into ingestion items using the configured field paths.</summary>
    /// <param name="json">The raw JSON response.</param>
    /// <param name="options">The resolved options.</param>
    /// <returns>The mapped items (empty when the items array cannot be resolved).</returns>
    internal static IReadOnlyList<IngestionItem> MapItems(string json, HttpApiSourceOptions options)
    {
        using var document = JsonDocument.Parse(json);
        var array = ResolveArray(document.RootElement, options.ItemsPath);
        if (array is null)
            return [];

        var items = new List<IngestionItem>();
        var index = 0;
        foreach (var element in array.Value.EnumerateArray())
        {
            var rawId = GetByPath(element, options.IdField) ?? index.ToString(CultureInfo.InvariantCulture);
            var title = GetByPath(element, options.TitleField) ?? rawId;
            var body = GetByPath(element, options.BodyField) ?? element.GetRawText();

            items.Add(new IngestionItem
            {
                Id = $"{options.SourceLabel}:{CleanId(rawId)}",
                Title = title,
                Body = body,
                SourceKind = options.SourceLabel,
                SourceUrl = GetByPath(element, options.UrlField),
                Domain = string.IsNullOrWhiteSpace(options.Domain) ? null : options.Domain,
            });
            index++;
        }

        return items;
    }

    private static JsonElement? ResolveArray(JsonElement root, string? itemsPath)
    {
        var current = root;
        if (!string.IsNullOrWhiteSpace(itemsPath))
        {
            foreach (var segment in itemsPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                    return null;
                current = next;
            }
        }

        return current.ValueKind == JsonValueKind.Array ? current : null;
    }

    private static string? GetByPath(JsonElement element, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var current = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                return null;
            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.GetRawText(),
        };
    }

    private static string CleanId(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? "item" : trimmed.Replace(' ', '-');
    }

    private static string? Setting(IReadOnlyDictionary<string, string> settings, string key)
        => settings.TryGetValue(key, out var value) ? value : null;

    private static HttpApiPageMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "page" => HttpApiPageMode.Page,
        "offset" => HttpApiPageMode.Offset,
        _ => HttpApiPageMode.None,
    };

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
