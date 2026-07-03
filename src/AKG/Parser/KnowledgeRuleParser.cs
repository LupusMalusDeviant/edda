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
        var appliesTo = GetList(fields, "appliesTo");
        var ownerId = GetString(fields, "ownerId");
        var tenantIdStr = GetString(fields, "tenantId");
        var author = GetString(fields, "author");
        var validatorScript = GetString(fields, "validatorScript");
        // F16: optional LLM-judge validator (validatorType: llm + a natural-language prompt).
        var validatorType = GetString(fields, "validatorType");
        var validatorPrompt = GetString(fields, "validatorPrompt");
        // F7 kill-switch: enabled unless the frontmatter explicitly sets `validatorEnabled: false`.
        var validatorEnabled = !string.Equals(GetString(fields, "validatorEnabled"), "false", StringComparison.OrdinalIgnoreCase);
        // F5: optional validator self-test fixtures (nested pass/fail block-scalar lists).
        var validatorFixtures = fields.TryGetValue("validatorFixtures", out var vfo)
            ? vfo as RuleValidatorFixtures
            : null;
        if (validatorFixtures is { } vf && vf.Pass.Count == 0 && vf.Fail.Count == 0)
            validatorFixtures = null;
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
            AppliesTo = appliesTo,
            ValidatorScript = string.IsNullOrWhiteSpace(validatorScript) ? null : validatorScript,
            ValidatorEnabled = validatorEnabled,
            ValidatorType = string.IsNullOrWhiteSpace(validatorType) ? null : validatorType,
            ValidatorPrompt = string.IsNullOrWhiteSpace(validatorPrompt) ? null : validatorPrompt,
            ValidatorFixtures = validatorFixtures,
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

            if (string.Equals(key, "validatorFixtures", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(value))
            {
                // F5: nested pass/fail lists of literal block scalars — a dedicated sub-parser.
                result[key] = ReadFixtures(lines, ref i);
                continue;
            }

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
            else if (IsBlockScalarIndicator(value))
            {
                // YAML block scalar (e.g. `validatorScript: |`): the following, more-indented lines form
                // a multi-line string with newlines preserved (literal) or folded to spaces (`>`).
                result[key] = ReadBlockScalar(lines, ref i, folded: value.StartsWith('>'));
            }
            else
            {
                result[key] = value;
                i++;
            }
        }

        return result;
    }

    /// <summary>Whether a frontmatter value is a YAML block-scalar indicator (<c>|</c>, <c>|-</c>, <c>&gt;</c>, <c>&gt;-</c>).</summary>
    private static bool IsBlockScalarIndicator(string value)
        => value is "|" or "|-" or "|+" or ">" or ">-" or ">+";

    /// <summary>
    /// Reads a YAML block-scalar body starting at the line after the indicator (<paramref name="i"/> points
    /// at the indicator line). Consumes all subsequent lines that are blank or indented beyond column 0;
    /// the common leading indentation is stripped and trailing blank lines are clipped. Advances
    /// <paramref name="i"/> past the block.
    /// </summary>
    /// <param name="lines">All frontmatter lines.</param>
    /// <param name="i">The current line index (at the indicator); advanced past the block on return.</param>
    /// <param name="folded">When <see langword="true"/> (<c>&gt;</c>), newlines are folded to single spaces.</param>
    /// <returns>The block-scalar content.</returns>
    internal static string ReadBlockScalar(string[] lines, ref int i, bool folded)
    {
        i++; // Move past the "key: |" indicator line.
        var blockLines = new List<string>();
        int? indent = null;

        while (i < lines.Length)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                blockLines.Add(string.Empty);
                i++;
                continue;
            }

            var leading = raw.Length - raw.AsSpan().TrimStart().Length;
            if (leading == 0)
                break; // Dedent to key level → the block has ended.

            indent ??= leading;
            var strip = Math.Min(indent.Value, leading);
            blockLines.Add(raw[strip..]);
            i++;
        }

        // Clip trailing blank lines (YAML default chomping).
        while (blockLines.Count > 0 && blockLines[^1].Length == 0)
            blockLines.RemoveAt(blockLines.Count - 1);

        return folded
            ? string.Join(' ', blockLines.Where(l => l.Length > 0))
            : string.Join('\n', blockLines);
    }

    /// <summary>
    /// Reads a <c>validatorFixtures:</c> block (nested <c>pass:</c>/<c>fail:</c> lists of literal
    /// block scalars). <paramref name="i"/> points at the <c>validatorFixtures:</c> line; it is
    /// advanced past the whole block.
    /// </summary>
    /// <param name="lines">All frontmatter lines.</param>
    /// <param name="i">The current line index (at the indicator); advanced past the block on return.</param>
    /// <returns>The parsed fixtures (empty <see cref="RuleValidatorFixtures.Pass"/>/<see cref="RuleValidatorFixtures.Fail"/> when none).</returns>
    private static RuleValidatorFixtures ReadFixtures(string[] lines, ref int i)
    {
        i++; // past "validatorFixtures:"
        var pass = new List<string>();
        var fail = new List<string>();
        while (i < lines.Length)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) { i++; continue; }

            var leading = raw.Length - raw.AsSpan().TrimStart().Length;
            if (leading == 0) break; // dedent to key level → the fixtures block ended

            var trimmed = raw.Trim();
            if (trimmed == "pass:") { i++; ReadFixtureItems(lines, ref i, leading, pass); }
            else if (trimmed == "fail:") { i++; ReadFixtureItems(lines, ref i, leading, fail); }
            else i++; // ignore unrecognized lines defensively
        }

        return new RuleValidatorFixtures { Pass = pass, Fail = fail };
    }

    /// <summary>
    /// Reads the <c>- |</c> block-scalar list items under a <c>pass:</c>/<c>fail:</c> sub-key.
    /// Stops when a line dedents to <paramref name="subKeyIndent"/> or less (sibling key / block end).
    /// </summary>
    /// <param name="lines">All frontmatter lines.</param>
    /// <param name="i">The current line index; advanced past the list on return.</param>
    /// <param name="subKeyIndent">Indent of the owning <c>pass:</c>/<c>fail:</c> sub-key.</param>
    /// <param name="items">Accumulator the parsed snippets are appended to.</param>
    private static void ReadFixtureItems(string[] lines, ref int i, int subKeyIndent, List<string> items)
    {
        while (i < lines.Length)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) { i++; continue; }

            var leading = raw.Length - raw.AsSpan().TrimStart().Length;
            if (leading <= subKeyIndent) break; // sibling key or dedent → list ended

            var trimmed = raw.TrimStart();
            if (!trimmed.StartsWith('-')) { i++; continue; } // defensive: skip non-item lines

            var afterDash = trimmed[1..].Trim();
            if (afterDash is "|" or "|-" or "|+")
            {
                i++; // move to the block body
                items.Add(ReadListItemScalar(lines, ref i, markerIndent: leading));
            }
            else if (afterDash.Length > 0)
            {
                items.Add(afterDash); // inline single-line item: "- some code"
                i++;
            }
            else
            {
                i++; // lone "-" → skip
            }
        }
    }

    /// <summary>
    /// Reads a literal block scalar that belongs to a list item whose <c>-</c> marker sits at
    /// <paramref name="markerIndent"/>. Body lines are indented beyond the marker; the common indent
    /// is stripped and trailing blank lines are clipped.
    /// </summary>
    /// <param name="lines">All frontmatter lines.</param>
    /// <param name="i">The current line index (first body line); advanced past the scalar on return.</param>
    /// <param name="markerIndent">Indent of the list item's <c>-</c> marker.</param>
    /// <returns>The block-scalar content with newlines preserved.</returns>
    private static string ReadListItemScalar(string[] lines, ref int i, int markerIndent)
    {
        var blockLines = new List<string>();
        int? indent = null;
        while (i < lines.Length)
        {
            var raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw)) { blockLines.Add(string.Empty); i++; continue; }

            var leading = raw.Length - raw.AsSpan().TrimStart().Length;
            if (leading <= markerIndent) break; // next item / sibling key / dedent

            indent ??= leading;
            var strip = Math.Min(indent.Value, leading);
            blockLines.Add(raw[strip..]);
            i++;
        }

        while (blockLines.Count > 0 && blockLines[^1].Length == 0)
            blockLines.RemoveAt(blockLines.Count - 1);

        return string.Join('\n', blockLines);
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
