using Edda.AKG.Ingestion.Globbing;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Mapping;

/// <summary>
/// Maps a source-neutral <see cref="IngestionItem"/> to a <see cref="KnowledgeRule"/>. Determines the
/// knowledge type, domain and priority from the configured <see cref="TypeMappingRule"/>s (with a
/// path-derived domain fallback), carries over author/created from the source frontmatter, and resolves
/// the item's native links into typed <see cref="RuleRelations"/>. Relations only ever reference ids
/// already known in the ingestion run, so no nodes are invented. Purely deterministic — no LLM.
/// </summary>
public sealed class IngestionItemMapper
{
    private const string DefaultType = "WorldKnowledge";
    private const string DefaultDomain = "general";

    /// <summary>
    /// Maps <paramref name="item"/> to a knowledge rule.
    /// </summary>
    /// <param name="item">The raw ingestion item produced by a source.</param>
    /// <param name="rules">Glob-to-type mapping rules; the first matching rule (by path) wins.</param>
    /// <param name="knownIds">
    /// Ids of all items in the current ingestion run. Native links are kept as relations only when their
    /// target is contained here.
    /// </param>
    /// <returns>A knowledge rule ready to be serialized and upserted into the graph.</returns>
    public KnowledgeRule Map(
        IngestionItem item,
        IReadOnlyList<TypeMappingRule> rules,
        IReadOnlySet<string> knownIds)
    {
        var relativePath = item.RelativePath ?? string.Empty;
        var match = rules.FirstOrDefault(rule => GlobMatcher.IsMatch(rule.GlobPattern, relativePath));

        var type = match?.Type ?? DefaultType;
        var domain = !string.IsNullOrWhiteSpace(item.Domain) ? item.Domain!
            : !string.IsNullOrWhiteSpace(match?.Domain) ? match!.Domain!
            : DomainFromPath(relativePath);
        var priority = match?.Priority ?? RulePriority.Medium;

        var author = item.RawFrontmatter.TryGetValue("author", out var a) && !string.IsNullOrWhiteSpace(a)
            ? a
            : null;

        DateOnly? created = null;
        if (item.RawFrontmatter.TryGetValue("created", out var createdRaw)
            && DateOnly.TryParse(createdRaw, out var parsed))
        {
            created = parsed;
        }

        return new KnowledgeRule
        {
            Id = item.Id,
            Type = type,
            Domain = domain,
            Priority = priority,
            Body = item.Body,
            Tags = item.Tags,
            Author = author,
            Created = created,
            RelatesTo = ResolveRelations(item.NativeLinks, knownIds),
            OwnerId = null,
            SourceType = item.SourceKind,
            SourceUrl = item.SourceUrl,
            ChunkStyle = item.ChunkStyle,
        };
    }

    private static string DomainFromPath(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 1 ? segments[0].ToLowerInvariant() : DefaultDomain;
    }

    private static RuleRelations? ResolveRelations(
        IReadOnlyList<IngestionLink> links,
        IReadOnlySet<string> knownIds)
    {
        var related = new List<string>();
        var supersedes = new List<string>();
        var requires = new List<string>();

        foreach (var link in links)
        {
            if (!knownIds.Contains(link.TargetRef))
                continue;

            switch (link.Kind.ToLowerInvariant())
            {
                case "supersedes":
                    AddUnique(supersedes, link.TargetRef);
                    break;
                case "requires":
                    AddUnique(requires, link.TargetRef);
                    break;
                default:
                    AddUnique(related, link.TargetRef);
                    break;
            }
        }

        if (related.Count == 0 && supersedes.Count == 0 && requires.Count == 0)
            return null;

        return new RuleRelations
        {
            Related = related,
            Supersedes = supersedes,
            Requires = requires,
        };
    }

    private static void AddUnique(List<string> target, string id)
    {
        if (!target.Contains(id))
            target.Add(id);
    }
}
