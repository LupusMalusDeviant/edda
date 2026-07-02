namespace Edda.Core.Models;

/// <summary>
/// Outcome of a memory-consolidation run. For a single-user run <see cref="UsersProcessed"/> is 1; for an
/// all-users run it counts the users consolidated and sums the removals across them.
/// </summary>
/// <param name="UsersProcessed">Number of users whose memory was consolidated.</param>
/// <param name="DuplicatesRemoved">Total normalized-duplicate memories removed.</param>
/// <param name="FadedRemoved">Total faded (below recall threshold) memories pruned.</param>
/// <param name="NearDuplicatesRemoved">Total token-similar near-duplicate memories merged away (C4).</param>
public sealed record MemoryConsolidationResult(
    int UsersProcessed,
    int DuplicatesRemoved,
    int FadedRemoved,
    int NearDuplicatesRemoved = 0)
{
    /// <summary>
    /// Bodies of the near-duplicate losers merged away this run. Populated only for single-user runs
    /// (<c>ConsolidateUserAsync</c>); empty for the aggregate all-users run so no cross-user content is
    /// collected into one list.
    /// </summary>
    public IReadOnlyList<string> MergedAwayBodies { get; init; } = [];

    /// <summary>Total memories removed (exact duplicates + near-duplicates + faded).</summary>
    public int TotalRemoved => DuplicatesRemoved + NearDuplicatesRemoved + FadedRemoved;
}
