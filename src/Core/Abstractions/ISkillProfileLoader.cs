using Edda.Core.Models;

namespace Edda.Core.Abstractions;

/// <summary>
/// Loads and caches skill profiles from the filesystem.
/// Skill profiles are Markdown files with YAML frontmatter that describe
/// a specialised agent's capabilities, focus areas, and constraints.
/// Built-in profiles are always available; user-defined profiles are stored in
/// <c>knowledge/skills/{name}.md</c> and take precedence over built-ins of the same name.
/// </summary>
public interface ISkillProfileLoader
{
    /// <summary>
    /// Loads a skill profile by name.
    /// Searches user-defined profiles (<c>knowledge/skills/{name}.md</c>) first,
    /// then falls back to built-in profiles.
    /// </summary>
    /// <param name="profileName">Profile name without extension (e.g., "researcher").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The parsed <see cref="SkillProfile"/>, or <see langword="null"/> if not found.
    /// </returns>
    Task<SkillProfile?> LoadAsync(string profileName, CancellationToken ct = default);

    /// <summary>
    /// Returns summaries of all available skill profiles (built-in + user-defined).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only list of <see cref="SkillProfileSummary"/> entries ordered built-in first.
    /// </returns>
    Task<IReadOnlyList<SkillProfileSummary>> ListAsync(CancellationToken ct = default);
}
