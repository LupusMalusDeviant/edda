namespace Edda.AKG.Graph;

/// <summary>
/// Derives a rule's dataset id from its provenance-prefixed rule id (ADR-0014). A dataset is a provenance
/// group — a single ingested source — whose head node is the two-segment id under the <c>git:</c> or
/// <c>upload:</c> prefix (e.g. <c>git:my-repo</c> for the rule <c>git:my-repo:docs/adr/0001</c>). Rules
/// without such a prefix (hand-authored rules, structural roots like <c>git-knowledge</c>) belong to no
/// dataset and are therefore never dataset-filtered.
/// </summary>
internal static class DatasetMembership
{
    private const string GitPrefix = "git:";
    private const string UploadPrefix = "upload:";
    private const char Separator = ':';

    /// <summary>
    /// Returns the dataset id a rule belongs to, or <see langword="null"/> when the rule is not part of a
    /// provenance group. The dataset id is the first two colon-separated segments of a <c>git:</c>/<c>upload:</c>
    /// rule id (the source head node); a bare prefix without a source segment yields <see langword="null"/>.
    /// </summary>
    /// <param name="ruleId">The rule id to inspect.</param>
    /// <returns>The <c>git:&lt;repo&gt;</c>/<c>upload:&lt;source&gt;</c> dataset id, or <see langword="null"/>.</returns>
    public static string? DatasetIdOf(string ruleId)
    {
        if (string.IsNullOrEmpty(ruleId)) return null;
        if (!ruleId.StartsWith(GitPrefix, StringComparison.Ordinal)
            && !ruleId.StartsWith(UploadPrefix, StringComparison.Ordinal))
            return null;

        var segments = ruleId.Split(Separator);
        // A dataset id needs "<prefix>:<source>" — a non-empty source segment. "git:"/"upload:" alone is not one.
        if (segments.Length < 2 || segments[1].Length == 0) return null;

        return $"{segments[0]}{Separator}{segments[1]}";
    }
}
