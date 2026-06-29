namespace Edda.AKG.Ingestion.Git;

/// <summary>
/// Derives stable, deterministic identifiers for items ingested from Git repositories. Ids have the
/// form <c>git:&lt;repo-slug&gt;:&lt;relative-path-without-extension&gt;</c> so that relations between
/// documents resolve to the same node across repeated ingestion runs.
/// </summary>
public static class GitItemIdentity
{
    /// <summary>
    /// Id of the shared root node that groups all knowledge ingested from Git repositories.
    /// Every per-repository node links to it, so ingested content forms one connected subgraph.
    /// </summary>
    public const string RootId = "git-knowledge";

    /// <summary>
    /// Builds the id of the synthetic per-repository node (e.g. <c>git:my-repo</c>) that all of a
    /// repository's file nodes attach to. Distinct from file ids, which carry a trailing path segment.
    /// </summary>
    /// <param name="slug">The repository slug from <see cref="Slug"/>.</param>
    /// <returns>A stable repository node id such as <c>git:my-repo</c>.</returns>
    public static string RepoId(string slug) => $"git:{slug}";

    /// <summary>Id of a Git host node (e.g. <c>git-host:git.example.com</c>).</summary>
    /// <param name="host">The host component of a repository URL.</param>
    public static string HostId(string host) => $"git-host:{host}";

    /// <summary>
    /// Id of a namespace/group node at a cumulative path (e.g. <c>git-group:grp/subgrp</c>), so that
    /// repositories sharing a namespace prefix attach to the same group nodes.
    /// </summary>
    /// <param name="cumulativePath">The slash-joined namespace path up to and including this group.</param>
    public static string GroupId(string cumulativePath) => $"git-group:{cumulativePath}";

    /// <summary>
    /// Parses a repository URL into its host, namespace (group) segments and repository name. HTTP(S)
    /// and SCP-like SSH URLs carry a host and namespace; bare local paths yield only the repository name
    /// (no host, no namespace), so a local mirror never produces a bogus group hierarchy.
    /// </summary>
    /// <param name="repositoryUrl">The repository URL or local path.</param>
    /// <returns>The host (or null), the ordered namespace segments, and the repository name.</returns>
    public static (string? Host, IReadOnlyList<string> Namespace, string Repo) Parse(string repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            return (null, [], "repo");

        var trimmed = repositoryUrl.Trim().Replace('\\', '/').TrimEnd('/');
        string? host;
        string pathPart;

        var schemeIndex = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            var afterScheme = trimmed[(schemeIndex + 3)..];
            var slash = afterScheme.IndexOf('/');
            host = slash >= 0 ? afterScheme[..slash] : afterScheme;
            pathPart = slash >= 0 ? afterScheme[(slash + 1)..] : string.Empty;
        }
        else if (TryParseScp(trimmed, out var scpHost, out var scpPath))
        {
            host = scpHost;
            pathPart = scpPath;
        }
        else
        {
            host = null;       // bare local path — no host, no meaningful namespace
            pathPart = trimmed;
        }

        if (host is not null)
        {
            var at = host.LastIndexOf('@');   // strip user[:pass]@ userinfo
            if (at >= 0)
                host = host[(at + 1)..];
        }

        var segments = pathPart.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return (host, [], "repo");

        var last = segments[^1];
        if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            last = last[..^4];
        var repo = string.IsNullOrWhiteSpace(last) ? "repo" : last;

        // Only real remotes (where a host was found) have a meaningful group namespace.
        var ns = host is not null && segments.Length > 1 ? segments[..^1] : [];
        return (host, ns, repo);
    }

    /// <summary>
    /// Recognizes an SCP-like SSH URL (<c>[user@]host:path</c>). Excludes Windows drive paths such as
    /// <c>C:/repo</c> by requiring the host part to look like a host (contain a dot or an '@').
    /// </summary>
    private static bool TryParseScp(string url, out string host, out string path)
    {
        host = string.Empty;
        path = string.Empty;

        var colon = url.IndexOf(':');
        if (colon <= 0)
            return false;

        var slash = url.IndexOf('/');
        if (slash >= 0 && slash < colon)
            return false;   // a path separator before the colon — not SCP-like

        var candidate = url[..colon];
        if (!candidate.Contains('.') && !candidate.Contains('@'))
            return false;   // e.g. a bare drive letter

        host = candidate;
        path = url[(colon + 1)..];
        return true;
    }

    /// <summary>
    /// Derives a short repository slug from a clone URL (the last path segment, with a trailing
    /// <c>.git</c> removed). Returns "repo" when no usable segment can be found.
    /// </summary>
    /// <param name="repositoryUrl">The repository URL (HTTPS or SSH form).</param>
    /// <returns>A slug such as "my-repo" for <c>https://gitlab.example/group/my-repo.git</c>.</returns>
    public static string Slug(string repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl))
            return "repo";

        var trimmed = repositoryUrl.Trim().Replace('\\', '/').TrimEnd('/');
        var lastSeparator = trimmed.LastIndexOfAny(['/', ':']);
        var name = lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return string.IsNullOrWhiteSpace(name) ? "repo" : name;
    }

    /// <summary>
    /// Builds the stable item id for a file at <paramref name="relativePath"/> within the repository
    /// identified by <paramref name="slug"/>. Path separators are normalized and the <c>.md</c>
    /// extension is stripped.
    /// </summary>
    /// <param name="slug">The repository slug from <see cref="Slug"/>.</param>
    /// <param name="relativePath">The file path relative to the repository root.</param>
    /// <returns>A stable id such as <c>git:my-repo:docs/adr/0001-foo</c>.</returns>
    public static string ItemId(string slug, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        normalized = normalized.TrimStart('/');

        if (normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^3];

        return $"git:{slug}:{normalized}";
    }
}
