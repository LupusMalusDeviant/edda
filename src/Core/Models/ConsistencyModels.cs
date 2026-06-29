namespace Edda.Core.Models;

/// <summary>
/// Manifest describing every file in a specific prototype version, including
/// per-file SHA-256 hash and the sub-agent that owns the file. Used by the
/// consistency keeper to preserve byte-identical carry-over across iterations.
/// </summary>
/// <param name="Version">Version number this manifest describes.</param>
/// <param name="CreatedAt">UTC timestamp when the version was assembled.</param>
/// <param name="Files">Relative file path (POSIX slashes) → entry.</param>
public sealed record ArtefactManifest(
    int Version,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, ArtefactEntry> Files);

/// <summary>
/// Single file entry inside an <see cref="ArtefactManifest"/>.
/// </summary>
/// <param name="Sha256">Uppercase hex SHA-256 hash of the UTF-8 content.</param>
/// <param name="Owner">Name of the sub-agent that produced this file (e.g. "ui", "backend", "security", "monolith").</param>
/// <param name="CreatedAt">UTC timestamp when this file was first written under this hash.</param>
public sealed record ArtefactEntry(
    string Sha256,
    string Owner,
    DateTimeOffset CreatedAt);

/// <summary>
/// Set of files produced by a single prototype sub-agent within a merge operation.
/// </summary>
/// <param name="AgentName">Identifier of the sub-agent (used as file owner in the manifest).</param>
/// <param name="Files">Relative file path → UTF-8 content.</param>
public sealed record AgentContribution(
    string AgentName,
    IReadOnlyDictionary<string, string> Files);

/// <summary>
/// Request to merge one or more <see cref="AgentContribution"/>s into a
/// versioned output directory while preserving byte-identical carry-over
/// of files that were not overwritten.
/// </summary>
/// <param name="ProjectId">Internal project identifier (from ASPS mapping store).</param>
/// <param name="Version">Version number the output directory represents.</param>
/// <param name="OutputDirectory">Target directory (created if missing) that will contain the merged files and <c>manifest.json</c>.</param>
/// <param name="Contributions">
/// All files produced by sub-agents in this build. When <paramref name="AllowAgentOverrides"/>
/// is true, list order defines precedence: later contributions overwrite earlier ones for the
/// same path (e.g. Security hardens a file written by Backend-Mock).
/// </param>
/// <param name="PreviousManifest">Manifest of the immediate predecessor version, or null for the first version.</param>
/// <param name="PreviousDirectory">Directory containing the predecessor's files, required when <paramref name="PreviousManifest"/> is set.</param>
/// <param name="AllowAgentOverrides">
/// When false (default) two agents writing the same relative path throw
/// <see cref="MergeConflictException"/>. When true the later contribution in
/// <paramref name="Contributions"/> wins and the override is logged at info level.
/// Used by the multi-agent orchestrator to let the Security sub-agent harden
/// files produced by UI and Backend-Mock.
/// </param>
public sealed record ConsistencyMergeRequest(
    string ProjectId,
    int Version,
    string OutputDirectory,
    IReadOnlyList<AgentContribution> Contributions,
    ArtefactManifest? PreviousManifest,
    string? PreviousDirectory,
    bool AllowAgentOverrides = false);

/// <summary>
/// Thrown when two sub-agents attempt to write the same relative file path
/// within a single merge request. Indicates an ownership design flaw
/// (sub-agents should have disjoint file responsibilities).
/// </summary>
public sealed class MergeConflictException : InvalidOperationException
{
    /// <summary>
    /// Relative file path that both agents attempted to write.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Name of the first agent that registered the path.
    /// </summary>
    public string FirstAgent { get; }

    /// <summary>
    /// Name of the second agent whose write caused the conflict.
    /// </summary>
    public string SecondAgent { get; }

    /// <summary>
    /// Initializes a new <see cref="MergeConflictException"/>.
    /// </summary>
    public MergeConflictException(string path, string firstAgent, string secondAgent)
        : base($"File '{path}' written by multiple agents ('{firstAgent}' and '{secondAgent}'). Resolve by assigning unique file ownership per sub-agent.")
    {
        Path = path;
        FirstAgent = firstAgent;
        SecondAgent = secondAgent;
    }
}
