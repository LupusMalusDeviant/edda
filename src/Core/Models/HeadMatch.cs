namespace Edda.Core.Models;

/// <summary>
/// A head (repository / upload source) matched in stage 1 of hierarchical coarse-to-fine retrieval, with
/// its best similarity score to the query. The head id is an id-prefix (e.g. <c>git:&lt;repo&gt;</c>) under
/// which the fine-grained chunk search is then scoped. See ADR-0009.
/// </summary>
/// <param name="HeadId">The matched head id / subtree prefix (e.g. <c>git:edda</c>).</param>
/// <param name="Score">Best cosine similarity of the query to any of the head's centroids.</param>
public sealed record HeadMatch(string HeadId, double Score);
