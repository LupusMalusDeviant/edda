using Edda.Core.Exceptions;
using Edda.Core.Models;

namespace Edda.AKG.Parser;

/// <summary>
/// Parses Markdown files with YAML frontmatter into <see cref="KnowledgeRule"/> instances.
/// Does not use YamlDotNet — uses a hand-written mini-parser to avoid external dependencies.
/// </summary>
/// <remarks>
/// Expected format:
/// <code>
/// ---
/// id: rule-001
/// title: My Rule
/// domain: csharp
/// priority: High
/// type: Rule
/// tags: [async, patterns]
/// ---
/// Rule body in Markdown...
/// </code>
/// </remarks>
public sealed class KnowledgeRuleParser
{
    /// <summary>
    /// Parses a Markdown string with YAML frontmatter and returns the corresponding <see cref="KnowledgeRule"/>.
    /// </summary>
    /// <param name="markdown">The full Markdown content including the YAML frontmatter block.</param>
    /// <returns>A fully populated <see cref="KnowledgeRule"/>.</returns>
    /// <exception cref="RuleParseException">
    /// Thrown when a required frontmatter field (<c>id</c> or <c>title</c>) is missing or empty.
    /// </exception>
    public KnowledgeRule Parse(string markdown)
    {
        var (frontmatter, body) = SplitFrontmatter(markdown);
        var fields = ParseFrontmatter(frontmatter);

        var id = GetString(fields, "id");
        if (string.IsNullOrWhiteSpace(id))
            throw new RuleParseException(string.Empty, "id");

        var title = GetString(fields, "title");
        if (string.IsNullOrWhiteSpace(title))
            throw new RuleParseException(string.Empty, "title");

        var domain = GetString(fields, "domain") ?? "general";
        var type = GetString(fields, "type") ?? "Rule";
        var priorityStr = GetString(fields, "priority");
        var priority = ParsePriority(priorityStr);
        var tags = GetList(fields, "tags");
        var implies = GetList(fields, "implies");
        var concepts = GetList(fields, "concepts");
        var conflictsWith = GetList(fields, "conflictsWith");
        var exceptionFor = GetList(fields, "exceptionFor");
        var requires = GetList(fields, "requires");
        var supersedes = GetList(fields, "supersedes");
        var related = GetList(fields, "related");
        var ownerId = GetString(fields, "ownerId");
        var tenantIdStr = GetString(fields, "tenantId");
        var author = GetString(fields, "author");
        var createdStr = GetString(fields, "created");
        DateOnly? created = null;
        if (!string.IsNullOrWhiteSpace(createdStr) && DateOnly.TryParse(createdStr, out var parsedDate))
            created = parsedDate;

        RuleRelations? relations = null;
        if (implies.Count > 0 || conflictsWith.Count > 0 || exceptionFor.Count > 0
            || requires.Count > 0 || supersedes.Count > 0 || related.Count > 0 || concepts.Count > 0)
        {
            relations = new RuleRelations
            {
                Implies = implies,
                ConflictsWith = conflictsWith,
                ExceptionFor = exceptionFor,
                Requires = requires,
                Supersedes = supersedes,
                Related = related,
            };
        }

        WhenRelevant? whenRelevant = null;
        if (concepts.Count > 0)
        {
            whenRelevant = new WhenRelevant
            {
                DetectedConcepts = concepts,
            };
        }

        return new KnowledgeRule
        {
            Id = id,
            Type = type,
            Domain = domain,
            Priority = priority,
            Body = body.Trim(),
            Tags = tags,
            OwnerId = ownerId,
            TenantId = string.IsNullOrWhiteSpace(tenantIdStr) ? Tenants.DefaultTenantId : tenantIdStr,
            Author = author,
            Created = created,
            RelatesTo = relations,
            WhenRelevant = whenRelevant,
        };
    }

    /// <summary>
    /// Splits the Markdown into frontmatter content and body.
    /// </summary>
    /// <param name="markdown">The full Markdown text.</param>
    /// <returns>
    /// A tuple of (frontmatter lines, body). If no frontmatter block is found,
    /// frontmatter is empty and body is the full text.
    /// </returns>
    internal static (string Frontmatter, string Body) SplitFrontmatter(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return (string.Empty, string.Empty);

        var lines = markdown.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return (string.Empty, markdown);

        var endIdx = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                endIdx = i;
                break;
            }
        }

        if (endIdx < 0)
            return (string.Empty, markdown);

        var frontmatter = string.Join("\n", lines[1..endIdx]);
        var body = endIdx + 1 < lines.Length
            ? string.Join("\n", lines[(endIdx + 1)..])
            : string.Empty;

        return (frontmatter, body);
    }

    /// <summary>
    /// Parses YAML frontmatter into a dictionary of raw string or list values.
    /// </summary>
    /// <param name="frontmatter">The YAML frontmatter block (without the --- delimiters).</param>
    /// <returns>Dictionary mapping field names to either a <c>string</c> or <c>List&lt;string&gt;</c>.</returns>
    internal static Dictionary<string, object> ParseFrontmatter(string frontmatter)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(frontmatter))
            return result;

        var lines = frontmatter.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            var colonIdx = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIdx < 0) { i++; continue; }

            var key = line[..colonIdx].Trim();
            var value = line[(colonIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(value))
            {
                // Check for multiline list: next lines starting with "  -"
                var listItems = new List<string>();
                i++;
                while (i < lines.Length && (lines[i].StartsWith("  -", StringComparison.Ordinal) || lines[i].StartsWith("- ", StringComparison.Ordinal)))
                {
                    var item = lines[i].TrimStart().TrimStart('-').Trim();
                    if (!string.IsNullOrEmpty(item))
                        listItems.Add(item);
                    i++;
                }

                if (listItems.Count > 0)
                    result[key] = listItems;
                else
                    result[key] = string.Empty;
            }
            else if (value.StartsWith('[') && value.EndsWith(']'))
            {
                // Inline list: [item1, item2]
                result[key] = ParseInlineList(value);
                i++;
            }
            else
            {
                result[key] = value;
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// Parses an inline YAML list value like <c>[item1, item2, item3]</c>.
    /// </summary>
    /// <param name="value">The bracketed list string.</param>
    /// <returns>Parsed list of trimmed, non-empty strings.</returns>
    internal static List<string> ParseInlineList(string value)
    {
        var inner = value.Trim().TrimStart('[').TrimEnd(']');
        return inner
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static string? GetString(Dictionary<string, object> fields, string key)
    {
        return fields.TryGetValue(key, out var val) ? val?.ToString() : null;
    }

    private static IReadOnlyList<string> GetList(Dictionary<string, object> fields, string key)
    {
        if (!fields.TryGetValue(key, out var val)) return [];
        if (val is List<string> list) return list;
        if (val is string s && !string.IsNullOrWhiteSpace(s))
            return ParseInlineList(s.StartsWith('[') ? s : $"[{s}]");
        return [];
    }

    private static RulePriority ParsePriority(string? value)
    {
        return value switch
        {
            "Critical" => RulePriority.Critical,
            "High" => RulePriority.High,
            "Low" => RulePriority.Low,
            _ => RulePriority.Medium,
        };
    }
}
