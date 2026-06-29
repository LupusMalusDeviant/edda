using System.Globalization;
using System.Text;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Markdown;

/// <summary>
/// Serializes a <see cref="KnowledgeRule"/> into a Markdown document with YAML frontmatter — the
/// counterpart to the AKG rule parser. Output is round-trip compatible: parsing the result reproduces
/// the rule's parser-visible fields (id, title, type, domain, priority, tags, author, created,
/// relations, body).
/// </summary>
public sealed class FrontmatterSerializer
{
    /// <summary>
    /// Serializes <paramref name="rule"/> to Markdown. The <paramref name="title"/> is written to the
    /// frontmatter because <see cref="KnowledgeRule"/> does not carry it as a field.
    /// </summary>
    /// <param name="rule">The rule to serialize.</param>
    /// <param name="title">The human-readable title to record in the frontmatter.</param>
    /// <returns>A Markdown document beginning with a YAML frontmatter block.</returns>
    public string Serialize(KnowledgeRule rule, string title)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("id: ").Append(rule.Id).Append('\n');
        sb.Append("title: ").Append(title).Append('\n');
        sb.Append("type: ").Append(rule.Type).Append('\n');
        sb.Append("domain: ").Append(rule.Domain).Append('\n');
        sb.Append("priority: ").Append(rule.Priority).Append('\n');

        if (rule.Tags.Count > 0)
            sb.Append("tags: ").Append(FormatList(rule.Tags)).Append('\n');
        if (!string.IsNullOrWhiteSpace(rule.Author))
            sb.Append("author: ").Append(rule.Author).Append('\n');
        if (rule.Created is { } created)
            sb.Append("created: ").Append(created.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        if (!string.IsNullOrWhiteSpace(rule.OwnerId))
            sb.Append("ownerId: ").Append(rule.OwnerId).Append('\n');

        if (rule.RelatesTo is { } relations)
        {
            AppendRelation(sb, "implies", relations.Implies);
            AppendRelation(sb, "requires", relations.Requires);
            AppendRelation(sb, "conflictsWith", relations.ConflictsWith);
            AppendRelation(sb, "exceptionFor", relations.ExceptionFor);
            AppendRelation(sb, "supersedes", relations.Supersedes);
            AppendRelation(sb, "related", relations.Related);
        }

        sb.Append("---\n\n");
        sb.Append(rule.Body);
        sb.Append('\n');
        return sb.ToString();
    }

    private static void AppendRelation(StringBuilder sb, string key, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
            sb.Append(key).Append(": ").Append(FormatList(values)).Append('\n');
    }

    private static string FormatList(IReadOnlyList<string> values)
        => "[" + string.Join(", ", values) + "]";
}
