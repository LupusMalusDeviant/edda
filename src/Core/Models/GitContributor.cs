namespace Edda.Core.Models;

/// <summary>
/// A repository contributor derived from Git history: a commit-author display name and how many commits
/// they authored. Email addresses are intentionally omitted to keep stored personal data minimal.
/// </summary>
/// <param name="Name">The commit author's display name.</param>
/// <param name="Commits">Number of commits this author wrote.</param>
public sealed record GitContributor(string Name, int Commits);
