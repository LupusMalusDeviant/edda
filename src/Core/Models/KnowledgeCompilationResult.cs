namespace Edda.Core.Models;

/// <summary>
/// Result of a <see cref="Edda.Core.Abstractions.IKnowledgeCompiler.CompileAsync"/> call.
/// Contains the rules that were saved, rules returned in preview mode, and any parse errors.
/// </summary>
public sealed record KnowledgeCompilationResult
{
    /// <summary>Rules that were successfully parsed and saved to the AKG. Empty when <see cref="WasPreview"/> is true.</summary>
    public IReadOnlyList<KnowledgeRule> SavedRules { get; init; } = [];

    /// <summary>Rules returned for review when <see cref="WasPreview"/> is true. Empty when saved.</summary>
    public IReadOnlyList<KnowledgeRule> PreviewRules { get; init; } = [];

    /// <summary>Parse error messages for rule blocks the LLM returned but that could not be parsed.</summary>
    public IReadOnlyList<string> ParseErrors { get; init; } = [];

    /// <summary>Number of rules skipped because a rule with the same ID already exists in the AKG.</summary>
    public int DuplicatesSkipped { get; init; }

    /// <summary>True when the caller requested preview mode (no rules were persisted).</summary>
    public bool WasPreview { get; init; }
}
