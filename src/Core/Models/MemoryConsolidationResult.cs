namespace Edda.Core.Models;

/// <summary>
/// Outcome of a memory-consolidation run. For a single-user run <see cref="UsersProcessed"/> is 1; for an
/// all-users run it counts the users consolidated and sums the removals across them.
/// </summary>
/// <param name="UsersProcessed">Number of users whose memory was consolidated.</param>
/// <param name="DuplicatesRemoved">Total normalized-duplicate memories removed.</param>
/// <param name="FadedRemoved">Total faded (below recall threshold) memories pruned.</param>
public sealed record MemoryConsolidationResult(
    int UsersProcessed,
    int DuplicatesRemoved,
    int FadedRemoved)
{
    /// <summary>Total memories removed (duplicates + faded).</summary>
    public int TotalRemoved => DuplicatesRemoved + FadedRemoved;
}
