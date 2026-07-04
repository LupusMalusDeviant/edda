namespace Edda.Core.Models;

/// <summary>
/// The set of datasets a caller may read, resolved per request from the dataset-permission model (ADR-0014).
/// A <em>dataset</em> is a provenance group — a single ingested source such as a Git repository or an upload.
/// Visibility is one of two shapes:
/// <list type="bullet">
///   <item><see cref="Unrestricted"/> — the caller sees every dataset; no dataset-level read filtering
///   applies. This is the behaviour-neutral default that preserves the pre-dataset owner/tenant scoping.</item>
///   <item><see cref="Restricted"/> — the caller may read only the enumerated dataset ids (plus any rule that
///   belongs to no dataset, mirroring the null-owner "global" semantics).</item>
/// </list>
/// </summary>
public sealed class DatasetVisibility
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    private DatasetVisibility(bool isUnrestricted, IReadOnlySet<string> visibleDatasetIds)
    {
        IsUnrestricted = isUnrestricted;
        VisibleDatasetIds = visibleDatasetIds;
    }

    /// <summary>The behaviour-neutral default: every dataset is visible and no read is filtered.</summary>
    public static DatasetVisibility Unrestricted { get; } = new(true, Empty);

    /// <summary>Whether the caller may see every dataset (no dataset-level read filtering applies).</summary>
    public bool IsUnrestricted { get; }

    /// <summary>
    /// The dataset ids the caller may read. Meaningful only when <see cref="IsUnrestricted"/> is
    /// <see langword="false"/>; empty for <see cref="Unrestricted"/>.
    /// </summary>
    public IReadOnlySet<string> VisibleDatasetIds { get; }

    /// <summary>
    /// Restricts visibility to the given dataset ids. An empty set means the caller sees only rules that
    /// belong to no dataset.
    /// </summary>
    /// <param name="visibleDatasetIds">The dataset ids the caller may read.</param>
    /// <returns>A restricted visibility over an ordinal-comparing copy of the given ids.</returns>
    public static DatasetVisibility Restricted(IEnumerable<string> visibleDatasetIds)
    {
        ArgumentNullException.ThrowIfNull(visibleDatasetIds);
        return new DatasetVisibility(false, new HashSet<string>(visibleDatasetIds, StringComparer.Ordinal));
    }
}
