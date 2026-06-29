using System.Collections.ObjectModel;

namespace Edda.Core.Models;

/// <summary>
/// A raw, source-neutral unit of knowledge produced by an <c>IIngestionSource</c> before it is mapped
/// to a <see cref="KnowledgeRule"/>. Carries the original content plus provenance and any native links
/// (e.g. "supersedes", "references") that the mapper later resolves into typed graph relations.
/// </summary>
public sealed record IngestionItem
{
    /// <summary>
    /// Stable, deterministic identifier of this item (e.g. <c>git:my-repo:docs/adr/0001-foo</c>).
    /// Relations between items reference this id, so it must be reproducible across ingestion runs.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Human-readable title (e.g. the document's first heading or file name).</summary>
    public required string Title { get; init; }

    /// <summary>The raw Markdown (or text) body of the item.</summary>
    public required string Body { get; init; }

    /// <summary>The kind of source this item came from (e.g. "git", "jira", "awork").</summary>
    public required string SourceKind { get; init; }

    /// <summary>Optional canonical URL of the item in the source system, for provenance.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Optional path of the item relative to the source root (for file-based sources).</summary>
    public string? RelativePath { get; init; }

    /// <summary>
    /// Optional explicit domain. When set, the mapper uses it verbatim instead of inferring the domain
    /// from the path or a type-mapping rule (used for synthetic structural nodes such as repo roots).
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>Searchable tags derived from the source.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Frontmatter already present in the source document, keyed by field name.</summary>
    public IReadOnlyDictionary<string, string> RawFrontmatter { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Raw links to other items, to be resolved into typed relations by the mapper.</summary>
    public IReadOnlyList<IngestionLink> NativeLinks { get; init; } = [];

    /// <summary>
    /// Optional forced chunking style carried through to the rule (<c>prose</c>/<c>markdown</c>/<c>code</c>/
    /// <c>table</c>); null = auto-detect.
    /// </summary>
    public string? ChunkStyle { get; init; }
}

/// <summary>
/// A raw link from one ingestion item to another, before resolution into a typed graph relation.
/// </summary>
public sealed record IngestionLink
{
    /// <summary>
    /// Raw link kind as detected in the source (e.g. "supersedes", "references", "related").
    /// The mapper translates this into a concrete <see cref="RuleRelations"/> edge where applicable.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>Raw reference to the target item; resolved to an item id by the mapper.</summary>
    public required string TargetRef { get; init; }
}

/// <summary>A single error encountered while ingesting one item. Collected, never thrown.</summary>
public sealed record IngestionError
{
    /// <summary>Id of the item that failed, if known.</summary>
    public string? ItemId { get; init; }

    /// <summary>Human-readable description of what went wrong.</summary>
    public required string Message { get; init; }
}

/// <summary>Counts and errors produced by an ingestion run. Best-effort: it never represents a thrown exception.</summary>
public sealed record IngestionResult
{
    /// <summary>Number of items newly created.</summary>
    public int Imported { get; init; }

    /// <summary>Number of existing items updated (upsert over a stable id).</summary>
    public int Updated { get; init; }

    /// <summary>Number of items intentionally skipped (e.g. no mapping rule matched).</summary>
    public int Skipped { get; init; }

    /// <summary>Number of items that failed to ingest. See <see cref="Errors"/> for details.</summary>
    public int Failed { get; init; }

    /// <summary>Collected per-item errors.</summary>
    public IReadOnlyList<IngestionError> Errors { get; init; } = [];

    /// <summary>Total number of items processed across all outcomes.</summary>
    public int TotalProcessed => Imported + Updated + Skipped + Failed;

    /// <summary>A shared empty result (nothing processed).</summary>
    public static IngestionResult Empty { get; } = new();
}

/// <summary>
/// A rule mapping source paths to a knowledge type. The first matching rule (by glob) wins, letting
/// callers ingest some paths as normative rules and others as descriptive world knowledge.
/// </summary>
public sealed record TypeMappingRule
{
    /// <summary>Glob pattern matched against an item's relative path (e.g. <c>docs/adr/**</c>).</summary>
    public required string GlobPattern { get; init; }

    /// <summary>Knowledge type assigned to matching items (e.g. "WorldKnowledge", "Constraint").</summary>
    public required string Type { get; init; }

    /// <summary>Optional domain assigned to matching items; null lets the source infer it (e.g. from the directory).</summary>
    public string? Domain { get; init; }

    /// <summary>Priority assigned to matching items. Defaults to <see cref="RulePriority.Medium"/>.</summary>
    public RulePriority Priority { get; init; } = RulePriority.Medium;
}

/// <summary>Source-specific configuration for an ingestion run.</summary>
public sealed record IngestionSourceConfig
{
    /// <summary>Repository URL to clone (Git sources).</summary>
    public string? RepositoryUrl { get; init; }

    /// <summary>
    /// Optional canonical/remote URL used only to derive the host/group hierarchy, when cloning happens
    /// from a different location (e.g. a local mirror). Falls back to <see cref="RepositoryUrl"/>.
    /// </summary>
    public string? CanonicalUrl { get; init; }

    /// <summary>Branch or tag to check out; null uses the repository default.</summary>
    public string? Reference { get; init; }

    /// <summary>Glob patterns selecting which files to ingest; empty means a source-defined default.</summary>
    public IReadOnlyList<string> IncludeGlobs { get; init; } = [];

    /// <summary>
    /// Optional username for authenticated clones. Populated server-side by a connector from stored
    /// credentials — the public ingest endpoint must never accept it from a client request.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Optional access token for private repositories. Populated server-side by a connector from the
    /// credential store — the public ingest endpoint must never accept it from a client request.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>Additional source-specific settings keyed by name (for sources beyond Git).</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        ReadOnlyDictionary<string, string>.Empty;
}

/// <summary>A request to run an ingestion against a configured source.</summary>
public sealed record IngestionRequest
{
    /// <summary>Selects which <c>IIngestionSource</c> to use (e.g. "git").</summary>
    public required string SourceKind { get; init; }

    /// <summary>Configuration passed to the selected source.</summary>
    public required IngestionSourceConfig Source { get; init; }

    /// <summary>Glob-to-type mapping rules applied to the fetched items.</summary>
    public IReadOnlyList<TypeMappingRule> TypeMapping { get; init; } = [];

    /// <summary>When true, the optional LLM enricher is applied (see ADR-0001). Defaults to false (local-only).</summary>
    public bool EnableEnrichment { get; init; }

    /// <summary>
    /// Root directory the generated Markdown files are written to. Null uses the pipeline default
    /// (a dedicated ingested-knowledge directory kept separate from hand-authored rules).
    /// </summary>
    public string? TargetDirectory { get; init; }
}
