namespace Edda.Core.Models;

/// <summary>
/// Outcome of a call to <see cref="Edda.Core.Abstractions.IKnowledgeSeeder.SeedIfEmptyAsync"/>.
/// </summary>
public sealed record KnowledgeSeedResult
{
    /// <summary>True when files were actually copied; false on skip or no-op.</summary>
    public bool Seeded { get; init; }

    /// <summary>Number of files copied from source to target. Zero when <see cref="Seeded"/> is false.</summary>
    public int FilesCopied { get; init; }

    /// <summary>
    /// Machine-readable reason code for the outcome.
    /// Values: <c>seeded</c>, <c>target_not_empty</c>, <c>source_missing</c>,
    /// <c>no_source_files</c>, <c>error</c>.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Source path that was read from (informational, for logging).</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Target path that was written to (informational, for logging).</summary>
    public string TargetPath { get; init; } = string.Empty;
}
