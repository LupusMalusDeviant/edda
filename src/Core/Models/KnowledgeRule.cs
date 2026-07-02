namespace Edda.Core.Models;

/// <summary>
/// A single rule in the Agent Knowledge Graph (AKG).
/// Parsed from YAML frontmatter + Markdown body. Stored as a Neo4j node.
/// </summary>
public sealed record KnowledgeRule
{
    /// <summary>Unique rule identifier in kebab-case (e.g. "use-async-await").</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Rule type discriminator.
    /// Valid values: Rule | Pattern | Convention | Constraint | Guideline | Policy.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Domain this rule belongs to (e.g. "csharp", "security", "architecture").</summary>
    public required string Domain { get; init; }

    /// <summary>Determines how strongly this rule is weighted during context compilation.</summary>
    public required RulePriority Priority { get; init; }

    /// <summary>
    /// Confidence multiplier [0.0–1.0] adjusted by the TDK engine based on validator outcomes.
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>Searchable tags for filtering and keyword matching.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Optional rule author for attribution.</summary>
    public string? Author { get; init; }

    /// <summary>Date the rule was created.</summary>
    public DateOnly? Created { get; init; }

    /// <summary>Graph relationships to other rules.</summary>
    public RuleRelations? RelatesTo { get; init; }

    /// <summary>Conditions under which this rule is relevant during context compilation.</summary>
    public WhenRelevant? WhenRelevant { get; init; }

    /// <summary>
    /// Optional Python validator script for TDK validation.
    /// Null means this rule is not TDK-validated.
    /// </summary>
    public string? ValidatorScript { get; init; }

    /// <summary>
    /// Whether this rule's TDK validator is active (F7 kill-switch). <see langword="false"/> disables the
    /// validator without deleting it — the TDK engine skips it. Defaults to <see langword="true"/>.
    /// </summary>
    public bool ValidatorEnabled { get; init; } = true;

    /// <summary>
    /// SHA-256 hash (lowercase hex) of <see cref="ValidatorScript"/>, persisted for confidence-history
    /// traceability (F7). Null when there is no validator. Recomputed from the script on each load.
    /// </summary>
    public string? ValidatorHash { get; init; }

    /// <summary>
    /// Optional list of source languages this rule's TDK validator targets (e.g. <c>python</c>,
    /// <c>csharp</c>). Empty means the validator applies to code blocks in any language. Lets the TDK
    /// engine skip a (rule × block) pair whose block language the rule does not target — before a
    /// sandbox is started — saving a container run and avoiding cross-language false positives.
    /// </summary>
    public IReadOnlyList<string> AppliesTo { get; init; } = [];

    /// <summary>The Markdown body of the rule — the actual content shown to the agent.</summary>
    public required string Body { get; init; }

    /// <summary>
    /// Owner scoping:
    /// null = global rule (operator/system, visible to all users).
    /// non-null = user-specific rule (only visible to the owning user).
    /// </summary>
    public string? OwnerId { get; init; }

    /// <summary>
    /// Optional source URL when the rule was compiled from web content.
    /// Null for manually authored rules.
    /// </summary>
    public string? SourceUrl { get; init; }

    /// <summary>
    /// Origin of this rule.
    /// Known values: <c>web</c> | <c>learnings</c> | <c>memory</c> | <c>text</c> | <c>manual</c>.
    /// Null for rules loaded from the knowledge/ filesystem.
    /// </summary>
    public string? SourceType { get; init; }

    /// <summary>
    /// Tenant this rule belongs to for logical multi-tenancy isolation (M3 / ADR-0012). Defaults to
    /// <see cref="Tenants.DefaultTenantId"/> — the single tenant of the standalone build — so existing
    /// data and callers that do not set it remain in the default tenant.
    /// </summary>
    public string TenantId { get; init; } = Tenants.DefaultTenantId;

    /// <summary>
    /// Optional forced chunking style for this document (<c>prose</c> | <c>markdown</c> | <c>code</c> |
    /// <c>table</c>); null lets the chunker auto-detect. Set when an upload specifies its chunking type.
    /// </summary>
    public string? ChunkStyle { get; init; }

    /// <summary>
    /// Bi-temporal valid-from timestamp: when the rule became valid in the real world.
    /// Null = valid since creation (unbounded past).
    /// </summary>
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>
    /// Bi-temporal valid-until timestamp: when the rule stopped being valid (e.g. when superseded).
    /// Null = still valid. Rules past their valid-until are excluded from context compilation but
    /// retained in the graph as history.
    /// </summary>
    public DateTimeOffset? ValidUntil { get; init; }

    /// <summary>ID of the rule that invalidated this one (e.g. via SUPERSEDES). Null if still valid.</summary>
    public string? InvalidatedBy { get; init; }
}

/// <summary>Priority level controlling how strongly a rule influences context compilation.</summary>
public enum RulePriority
{
    /// <summary>Low weight; included only when highly relevant.</summary>
    Low,

    /// <summary>Standard weight.</summary>
    Medium,

    /// <summary>High weight; included broadly.</summary>
    High,

    /// <summary>Always included regardless of relevance scoring.</summary>
    Critical
}

/// <summary>Defines typed relationships from this rule to other rules in the graph.</summary>
public sealed record RuleRelations
{
    /// <summary>Rules that are implied (transitively activated) by this rule.</summary>
    public IReadOnlyList<string> Implies { get; init; } = [];

    /// <summary>Rules that are mutually exclusive with this rule.</summary>
    public IReadOnlyList<string> ConflictsWith { get; init; } = [];

    /// <summary>Rules for which this rule serves as an exception.</summary>
    public IReadOnlyList<string> ExceptionFor { get; init; } = [];

    /// <summary>Rules that must be active for this rule to apply.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>Older rules that this rule replaces.</summary>
    public IReadOnlyList<string> Supersedes { get; init; } = [];

    /// <summary>Related rules without a strict semantic relationship.</summary>
    public IReadOnlyList<string> Related { get; init; } = [];
}

/// <summary>Conditions that determine when a rule is considered relevant for a task.</summary>
public sealed record WhenRelevant
{
    /// <summary>Glob patterns matching file paths that trigger this rule's relevance.</summary>
    public IReadOnlyList<string> FilePatterns { get; init; } = [];

    /// <summary>Task type keywords that trigger this rule's relevance.</summary>
    public IReadOnlyList<string> TaskTypes { get; init; } = [];

    /// <summary>Concept keywords extracted from the task that trigger this rule.</summary>
    public IReadOnlyList<string> DetectedConcepts { get; init; } = [];
}
