using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Edda.AKG.Ingestion.Markdown;
using Edda.AKG.Ingestion.Mapping;
using Edda.AKG.Ingestion.Sources;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Import;

/// <summary>
/// Imports uploaded knowledge into the graph using the same mapping as the Git ingestion, so uploads form
/// a connected subgraph (root → source node → items) instead of flat nodes. Supports:
/// a JSON <see cref="KnowledgeBundle"/> (lossless rule import); Markdown (single / ZIP collection) and PDF;
/// HTML (single / in ZIP, tags stripped — e.g. Confluence/Notion exports); and record formats CSV, JSONL
/// and JSON arrays (each record → an item — covers vector-DB dumps, whose stored text is imported while the
/// model-specific vectors are discarded and recomputed locally, see ADR-0007). Best-effort: per-item
/// failures are collected, never thrown.
/// </summary>
public sealed class KnowledgeImporter : IKnowledgeImporter
{
    private const string MarkdownFilter = ".md";
    private const string HtmlFilter = ".html";
    private const string ImportedTag = "imported";

    // Uploads are NOT Git repos: they get their own hierarchy (uploads → upload:<source> → files) instead
    // of being grafted onto the Git knowledge root, so the graph models them honestly.
    private const string UploadRootId = "uploads";
    private const string UploadDomain = "uploads";

    private readonly IKnowledgeGraph _graph;
    private readonly IArchiveExtractor _archive;
    private readonly IPdfTextExtractor _pdf;
    private readonly bool _allowImportedValidators;
    private readonly IngestionItemMapper _mapper = new();

    /// <summary>Initializes a new instance of the <see cref="KnowledgeImporter"/> class.</summary>
    /// <param name="graph">The knowledge graph imported rules are upserted into.</param>
    /// <param name="archive">Extractor for ZIP collections.</param>
    /// <param name="pdf">Extractor for PDF text.</param>
    /// <param name="allowImportedValidators">
    /// When <see langword="false"/> (the default), <c>validatorScript</c> is stripped from imported bundle
    /// rules so foreign bundles cannot smuggle executable TDK validators into the graph — analogous to the
    /// vector hygiene of record imports. Set via <c>IMPORT_ALLOW_VALIDATORS=true</c> for trusted bundles.
    /// </param>
    public KnowledgeImporter(
        IKnowledgeGraph graph, IArchiveExtractor archive, IPdfTextExtractor pdf, bool allowImportedValidators = false)
    {
        _graph = graph;
        _archive = archive;
        _pdf = pdf;
        _allowImportedValidators = allowImportedValidators;
    }

    /// <inheritdoc />
    public async Task<IngestionResult> ImportAsync(
        string fileName,
        byte[] content,
        string? domain,
        string? chunkStyle = null,
        CancellationToken cancellationToken = default)
    {
        var style = NormalizeStyle(chunkStyle);
        switch (Extension(fileName))
        {
            case ".json":
                return await ImportJsonAsync(fileName, content, domain, style, cancellationToken).ConfigureAwait(false);
            case ".jsonl":
                return await ImportRecordsAsync(fileName, ParseJsonl(Utf8(content)), domain, style, cancellationToken).ConfigureAwait(false);
            case ".csv":
                return await ImportRecordsAsync(fileName, ParseCsv(Utf8(content)), domain, style, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<ArchiveTextEntry> entries;
        try
        {
            entries = Collect(fileName, content);
        }
        catch (Exception ex)
        {
            return Failed(fileName, ex.Message);
        }

        if (entries.Count == 0)
            return Failed(fileName, "Keine importierbaren Inhalte gefunden.");

        var slug = Slugify(string.IsNullOrWhiteSpace(domain) ? Stem(FileNameOf(fileName)) : domain!);
        var contentItems = entries
            .Select(entry => BuildUploadItem(slug, entry.Path, entry.Content, style))
            .ToList();

        return await IngestItemsAsync(slug, contentItems, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<ArchiveTextEntry> Collect(string fileName, byte[] content) => Extension(fileName) switch
    {
        ".md" or ".markdown" =>
            [new ArchiveTextEntry { Path = FileNameOf(fileName), Content = Utf8(content) }],
        ".html" or ".htm" =>
            [new ArchiveTextEntry { Path = ToMarkdownName(FileNameOf(fileName)), Content = StripHtml(Utf8(content)) }],
        ".zip" => CollectZip(content),
        ".pdf" =>
            [new ArchiveTextEntry { Path = ToMarkdownName(FileNameOf(fileName)), Content = _pdf.Extract(content) }],
        var other => throw new NotSupportedException(
            $"Nicht unterstütztes Format '{other}'. Erlaubt: .md, .zip, .pdf, .html, .json, .jsonl, .csv."),
    };

    private IReadOnlyList<ArchiveTextEntry> CollectZip(byte[] zip)
    {
        var entries = new List<ArchiveTextEntry>(_archive.ExtractTextEntries(zip, MarkdownFilter));
        foreach (var html in _archive.ExtractTextEntries(zip, HtmlFilter))
            entries.Add(new ArchiveTextEntry { Path = ToMarkdownName(html.Path), Content = StripHtml(html.Content) });
        return entries;
    }

    /// <summary>Wraps content items with the shared root + source-node hierarchy, maps and upserts them.</summary>
    private async Task<IngestionResult> IngestItemsAsync(
        string slug,
        IReadOnlyList<IngestionItem> contentItems,
        CancellationToken cancellationToken)
    {
        var items = new List<IngestionItem>
        {
            BuildUploadRoot(),
            BuildUploadSource(slug),
        };
        items.AddRange(contentItems);

        items = items.GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).ToList();
        var knownIds = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var imported = 0;
        var failed = 0;
        var errors = new List<IngestionError>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _graph.UpsertRuleAsync(_mapper.Map(item, [], knownIds), cancellationToken).ConfigureAwait(false);
                imported++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add(new IngestionError { ItemId = item.Id, Message = ex.Message });
            }
        }

        return new IngestionResult { Imported = imported, Failed = failed, Errors = errors };
    }

    private async Task<IngestionResult> ImportJsonAsync(
        string fileName, byte[] content, string? domain, string? chunkStyle, CancellationToken ct)
    {
        var text = Utf8(content);
        List<IReadOnlyDictionary<string, string?>>? records = null;
        var isObject = false;
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                records = document.RootElement.EnumerateArray()
                    .Where(element => element.ValueKind == JsonValueKind.Object)
                    .Select(ToRecord)
                    .ToList();
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                isObject = true;
            }
        }
        catch (Exception ex)
        {
            return Failed(fileName, $"JSON ungültig: {ex.Message}");
        }

        if (records is not null)
            return await ImportRecordsAsync(fileName, records, domain, chunkStyle, ct).ConfigureAwait(false);
        if (isObject)
            return await ImportBundleAsync(fileName, text, ct).ConfigureAwait(false);
        return Failed(fileName, "JSON muss ein Objekt (Bündel) oder ein Array (Datensätze) sein.");
    }

    private async Task<IngestionResult> ImportBundleAsync(string fileName, string json, CancellationToken ct)
    {
        KnowledgeBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<KnowledgeBundle>(json, KnowledgeBundleSerialization.Options);
        }
        catch (Exception ex)
        {
            return Failed(fileName, $"JSON ungültig: {ex.Message}");
        }

        if (bundle is null || bundle.Rules.Count == 0)
            return Failed(fileName, "Kein gültiges Wissensbündel (Feld 'rules' fehlt oder ist leer).");

        var imported = 0;
        var failed = 0;
        var errors = new List<IngestionError>();
        foreach (var rule in bundle.Rules)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // F17: never accept executable validators from a foreign bundle unless explicitly allowed.
                var toImport = _allowImportedValidators || rule.ValidatorScript is null
                    ? rule
                    : rule with { ValidatorScript = null };
                await _graph.UpsertRuleAsync(toImport, ct).ConfigureAwait(false);
                imported++;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add(new IngestionError { ItemId = rule.Id, Message = ex.Message });
            }
        }

        return new IngestionResult { Imported = imported, Failed = failed, Errors = errors };
    }

    private async Task<IngestionResult> ImportRecordsAsync(
        string fileName,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> records,
        string? domain,
        string? chunkStyle,
        CancellationToken ct)
    {
        if (records.Count == 0)
            return Failed(fileName, "Keine Datensätze gefunden.");

        var slug = Slugify(string.IsNullOrWhiteSpace(domain) ? Stem(FileNameOf(fileName)) : domain!);
        var contentItems = records.Select((record, index) => RecordToItem(slug, index, record, chunkStyle)).ToList();
        return await IngestItemsAsync(slug, contentItems, ct).ConfigureAwait(false);
    }

    private static IngestionItem RecordToItem(
        string slug, int index, IReadOnlyDictionary<string, string?> fields, string? chunkStyle)
    {
        var id = First(fields, "id", "key", "uuid") ?? index.ToString(CultureInfo.InvariantCulture);
        var body = First(fields, "body", "content", "text", "document", "description") ?? JoinFields(fields);
        var title = First(fields, "title", "name", "summary") ?? FirstLine(body) ?? $"{slug} #{index + 1}";

        return new IngestionItem
        {
            Id = $"{UploadSourceId(slug)}:{CleanId(id)}",
            Title = title,
            Body = body,
            SourceKind = "upload",
            SourceUrl = First(fields, "url", "link", "self"),
            Tags = [ImportedTag],
            ChunkStyle = chunkStyle,
            NativeLinks = [new IngestionLink { Kind = "related", TargetRef = UploadSourceId(slug) }],
        };
    }

    private static string? NormalizeStyle(string? style)
    {
        var normalized = style?.Trim().ToLowerInvariant();
        return normalized is "prose" or "markdown" or "code" or "table" ? normalized : null;
    }

    private static string UploadSourceId(string slug) => $"upload:{slug}";

    private static string UploadItemId(string slug, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];
        return $"upload:{slug}:{normalized}";
    }

    /// <summary>Idempotent root grouping all uploaded sources (distinct from the Git knowledge root).</summary>
    private static IngestionItem BuildUploadRoot() => new()
    {
        Id = UploadRootId,
        Title = "Uploads",
        Body = "Wurzelknoten für hochgeladene Wissensquellen (Dateien — keine Git-Repositories).",
        SourceKind = "upload",
        Domain = UploadDomain,
        Tags = ["upload"],
    };

    /// <summary>Per-upload source node that the uploaded files attach to, linked to the uploads root.</summary>
    private static IngestionItem BuildUploadSource(string slug) => new()
    {
        Id = UploadSourceId(slug),
        Title = slug,
        Body = $"Hochgeladene Quelle '{slug}'.",
        SourceKind = "upload",
        Domain = UploadDomain,
        Tags = ["upload", slug.ToLowerInvariant()],
        NativeLinks = [new IngestionLink { Kind = "related", TargetRef = UploadRootId }],
    };

    /// <summary>
    /// Builds a content item from an uploaded file, attached to its upload source node. Intra-upload
    /// Markdown links are resolved to sibling item ids (so a ZIP of cross-referencing docs stays connected).
    /// </summary>
    private static IngestionItem BuildUploadItem(string slug, string relativePath, string content, string? chunkStyle)
    {
        var (frontmatter, body) = MarkdownFrontmatter.Split(content);
        var title = frontmatter.TryGetValue("title", out var fmTitle) && !string.IsNullOrWhiteSpace(fmTitle)
            ? fmTitle
            : MarkdownFrontmatter.FirstHeading(body) ?? GitMarkdownSource.FileName(relativePath);

        var links = MarkdownFrontmatter.MarkdownLinks(body)
            .Select(target => ResolveUploadLink(slug, relativePath, target))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(targetId => new IngestionLink { Kind = "related", TargetRef = targetId })
            .ToList();
        links.Add(new IngestionLink { Kind = "related", TargetRef = UploadSourceId(slug) });

        return new IngestionItem
        {
            Id = UploadItemId(slug, relativePath),
            Title = title,
            Body = body.Trim(),
            SourceKind = "upload",
            RelativePath = relativePath,
            Tags = [.. GitMarkdownSource.PathTags(relativePath), ImportedTag],
            RawFrontmatter = frontmatter,
            ChunkStyle = chunkStyle,
            NativeLinks = links,
        };
    }

    /// <summary>Resolves a repository-relative Markdown link to a sibling upload item id (ignores external/absolute).</summary>
    private static string? ResolveUploadLink(string slug, string fromRelativePath, string linkTarget)
    {
        if (linkTarget.Contains("://", StringComparison.Ordinal) || linkTarget.StartsWith('/'))
            return null;

        var combined = GitMarkdownSource.CombineRelative(
            GitMarkdownSource.DirectoryOf(fromRelativePath), linkTarget);
        return combined is null ? null : UploadItemId(slug, combined);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ParseJsonl(string text)
    {
        var records = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    records.Add(ToRecord(document.RootElement));
            }
            catch (JsonException)
            {
                // Skip malformed lines (best-effort).
            }
        }

        return records;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ParseCsv(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();
        if (lines.Count < 2)
            return [];

        var headers = ParseCsvLine(lines[0]);
        var records = new List<IReadOnlyDictionary<string, string?>>();
        for (var i = 1; i < lines.Count; i++)
        {
            var values = ParseCsvLine(lines[i]);
            var record = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < headers.Count; column++)
                record[headers[column]] = column < values.Count ? values[column] : null;
            records.Add(record);
        }

        return records;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        fields.Add(builder.ToString());
        return fields;
    }

    private static IReadOnlyDictionary<string, string?> ToRecord(JsonElement element)
    {
        var record = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            record[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => property.Value.GetRawText(),
            };
        }

        return record;
    }

    private static string StripHtml(string html)
    {
        var withoutBlocks = Regex.Replace(
            html, "<(script|style)[^>]*>.*?</\\1>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(3));
        var withoutTags = Regex.Replace(withoutBlocks, "<[^>]+>", " ", RegexOptions.None, TimeSpan.FromSeconds(3));
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, "\\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(3)).Trim();
    }

    private static string? First(IReadOnlyDictionary<string, string?> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string JoinFields(IReadOnlyDictionary<string, string?> fields)
        => string.Join('\n', fields
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{kv.Key}: {kv.Value}"));

    private static string? FirstLine(string text)
    {
        var newline = text.IndexOfAny(['\n', '\r']);
        var line = (newline >= 0 ? text[..newline] : text).Trim().TrimStart('#').Trim();
        return line.Length == 0 ? null : line.Length > 120 ? line[..120] : line;
    }

    private static string Utf8(byte[] content) => Encoding.UTF8.GetString(content);

    private static string Extension(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot >= 0 ? fileName[dot..].ToLowerInvariant() : string.Empty;
    }

    private static string FileNameOf(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string ToMarkdownName(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return (dot > 0 ? fileName[..dot] : fileName) + ".md";
    }

    private static string Stem(string fileName)
    {
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    private static string CleanId(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? "record" : trimmed.Replace(' ', '-');
    }

    private static string Slugify(string value)
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
        return slug.Length == 0 ? "import" : slug;
    }

    private static IngestionResult Failed(string itemId, string message) =>
        new() { Failed = 1, Errors = [new IngestionError { ItemId = itemId, Message = message }] };
}
