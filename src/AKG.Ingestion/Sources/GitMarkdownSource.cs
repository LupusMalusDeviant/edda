using System.Runtime.CompilerServices;
using Edda.AKG.Ingestion.Git;
using Edda.AKG.Ingestion.Globbing;
using Edda.AKG.Ingestion.Markdown;
using Edda.Core.Abstractions;
using Edda.Core.Models;

namespace Edda.AKG.Ingestion.Sources;

/// <summary>
/// Ingestion source for Git repositories. Clones the repository via <see cref="IGitClient"/>, scans
/// its Markdown files using deterministic conventions (no LLM), and yields source-neutral
/// <see cref="IngestionItem"/>s with stable ids, titles, path-derived tags and resolved cross-document
/// links. Assigning a knowledge type/domain and typing relations happens in a later mapping stage.
/// </summary>
public sealed class GitMarkdownSource : IIngestionSource
{
    private readonly IGitClient _git;
    private readonly IFileSystem _fileSystem;

    /// <summary>Initializes a new instance of the <see cref="GitMarkdownSource"/> class.</summary>
    /// <param name="git">Client used to clone the repository into a local working copy.</param>
    /// <param name="fileSystem">File system used to read the cloned working copy.</param>
    public GitMarkdownSource(IGitClient git, IFileSystem fileSystem)
    {
        _git = git;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public string SourceKind => "git";

    /// <summary>File extensions ingested from a repository: source code plus lightweight docs/config.</summary>
    private static readonly HashSet<string> IngestibleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Docs
        ".md", ".markdown", ".mdx", ".txt", ".rst", ".adoc",
        // .NET
        ".cs", ".fs", ".vb", ".razor", ".cshtml", ".xaml", ".csproj", ".fsproj", ".props", ".targets",
        // JS / TS / web
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".vue", ".svelte",
        ".css", ".scss", ".sass", ".less", ".html", ".htm",
        // JVM
        ".java", ".kt", ".kts", ".scala", ".groovy",
        // Python / Ruby / PHP / Go / Rust / Swift / others
        ".py", ".rb", ".php", ".go", ".rs", ".swift", ".dart", ".lua", ".r", ".jl", ".pl",
        // Native
        ".c", ".h", ".cpp", ".cc", ".cxx", ".hpp", ".hh",
        // Shell / infra
        ".sh", ".bash", ".zsh", ".ps1", ".psm1", ".bat", ".cmd", ".tf",
        // Query / schema / config
        ".sql", ".graphql", ".gql", ".proto", ".json", ".jsonc", ".yaml", ".yml", ".toml", ".ini", ".xml",
    };

    /// <summary>Directory names skipped wholesale (dependencies, build output, VCS, IDE, caches).</summary>
    private static readonly HashSet<string> IgnoredDirectorySegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bower_components", "vendor", "dist", "build", "out", "bin", "obj",
        "target", "packages", ".vs", ".vscode", ".idea", "__pycache__", ".pytest_cache", ".mypy_cache",
        "coverage", ".next", ".nuxt", ".svelte-kit", ".terraform", "venv", ".venv",
    };

    /// <summary>Specific high-volume, low-value files skipped by name.</summary>
    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "composer.lock",
        "Cargo.lock", "poetry.lock", "Gemfile.lock", "go.sum",
    };

    /// <summary>
    /// Decides whether a repository file is ingested: it must carry a supported source/doc extension and
    /// must not live in a dependency/build directory or be a lockfile, minified bundle or source map.
    /// Internal for unit testing.
    /// </summary>
    /// <param name="relativePath">The file path relative to the repository root.</param>
    internal static bool IsIngestibleFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (IgnoredDirectorySegments.Contains(segments[i]))
                return false;
        }

        var fileName = segments.Length > 0 ? segments[^1] : normalized;
        if (IgnoredFileNames.Contains(fileName)
            || fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && IngestibleExtensions.Contains(fileName[dot..]);
    }

    private static bool IsMarkdown(string relativePath)
        => relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || relativePath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
        || relativePath.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async IAsyncEnumerable<IngestionItem> FetchAsync(
        IngestionSourceConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.RepositoryUrl))
            yield break;

        var workingCopy = await _git.CloneAsync(
            new GitCloneRequest
            {
                RepositoryUrl = config.RepositoryUrl,
                Reference = config.Reference,
                Username = config.Username,
                Token = config.Token,
            },
            cancellationToken).ConfigureAwait(false);

        try
        {
            // Derive the slug + hierarchy from the canonical URL (falls back to the clone URL), so a
            // local mirror still yields the real host/group structure.
            var canonical = string.IsNullOrWhiteSpace(config.CanonicalUrl)
                ? config.RepositoryUrl
                : config.CanonicalUrl;
            var slug = GitItemIdentity.Slug(canonical!);
            var root = workingCopy.LocalPath;

            // Structural hierarchy: root -> host -> group… -> repo -> files, so repositories that share
            // a host or namespace attach to the same nodes instead of floating flat under one root.
            var (hierarchy, repoParentId) = BuildHierarchy(canonical);
            foreach (var node in hierarchy)
                yield return node;
            var contributors = _git.GetContributors(workingCopy);
            yield return BuildRepoItem(slug, config.RepositoryUrl, repoParentId, contributors);

            foreach (var file in _fileSystem.EnumerateFiles(root, "*", recursive: true))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = ToRelativePath(root, file);
                if (!IsIngestibleFile(relativePath))
                    continue;
                if (config.IncludeGlobs.Count > 0
                    && !config.IncludeGlobs.Any(glob => GlobMatcher.IsMatch(glob, relativePath)))
                {
                    continue;
                }

                // Skip files that cannot be read (e.g. a dangling symlink such as a build-generated
                // compile_commands.json) — one unreadable file must never abort the whole repository scan.
                string content;
                try
                {
                    content = await _fileSystem.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    continue;
                }

                yield return BuildItem(slug, relativePath, content);
            }
        }
        finally
        {
            // Keep clones transient — remove the working copy once scanning is done (or aborted).
            _git.Cleanup(workingCopy);
        }
    }

    /// <summary>
    /// Builds a single <see cref="IngestionItem"/> from a Markdown file's path and content. Internal to
    /// allow direct unit testing without a clone.
    /// </summary>
    internal static IngestionItem BuildItem(string slug, string relativePath, string content)
    {
        // Non-Markdown (code/config) files are ingested verbatim — no frontmatter split or link extraction.
        // The file extension carried in the id lets the chunker pick the right (code/table/…) splitting.
        if (!IsMarkdown(relativePath))
            return BuildCodeItem(slug, relativePath, content);

        var (frontmatter, body) = MarkdownFrontmatter.Split(content);

        var title = frontmatter.TryGetValue("title", out var fmTitle) && !string.IsNullOrWhiteSpace(fmTitle)
            ? fmTitle
            : MarkdownFrontmatter.FirstHeading(body) ?? FileName(relativePath);

        var links = MarkdownFrontmatter.MarkdownLinks(body)
            .Select(target => ResolveLink(slug, relativePath, target))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(targetId => new IngestionLink { Kind = "related", TargetRef = targetId })
            .ToList();

        // Attach the file to its repository node so it is always connected in the graph.
        links.Add(new IngestionLink { Kind = "related", TargetRef = GitItemIdentity.RepoId(slug) });

        return new IngestionItem
        {
            Id = GitItemIdentity.ItemId(slug, relativePath),
            Title = title,
            Body = body.Trim(),
            SourceKind = "git",
            RelativePath = relativePath,
            Tags = PathTags(relativePath),
            RawFrontmatter = frontmatter,
            NativeLinks = links,
        };
    }

    /// <summary>
    /// Builds an <see cref="IngestionItem"/> for a non-Markdown (code/config) file: the body is the raw file
    /// content and the item is linked to its repository node. Internal for direct unit testing.
    /// </summary>
    /// <param name="slug">The repository slug.</param>
    /// <param name="relativePath">The file path relative to the repository root.</param>
    /// <param name="content">The raw file content.</param>
    internal static IngestionItem BuildCodeItem(string slug, string relativePath, string content) => new()
    {
        Id = GitItemIdentity.ItemId(slug, relativePath),
        Title = relativePath,
        Body = content.Trim(),
        SourceKind = "git",
        RelativePath = relativePath,
        Tags = PathTags(relativePath),
        NativeLinks = [new IngestionLink { Kind = "related", TargetRef = GitItemIdentity.RepoId(slug) }],
    };

    /// <summary>
    /// Builds the shared <c>git-knowledge</c> root node that groups all Git-ingested knowledge.
    /// Idempotent across runs and repositories (always the same id).
    /// </summary>
    internal static IngestionItem BuildRootItem() => new()
    {
        Id = GitItemIdentity.RootId,
        Title = "Git Knowledge",
        Body = "Root node that groups all knowledge ingested from Git repositories.",
        SourceKind = "git",
        Domain = "git-knowledge",
        Tags = ["git-knowledge"],
    };

    /// <summary>Maximum number of contributors listed on a repository node's body.</summary>
    private const int MaxRepoContributors = 25;

    /// <summary>
    /// Builds the per-repository node that a repository's files attach to, itself linked to the
    /// shared <c>git-knowledge</c> root. Records the repository's Git contributors (commit authors and
    /// counts) in the node body so the graph captures who worked on it.
    /// </summary>
    /// <param name="slug">The repository slug.</param>
    /// <param name="repositoryUrl">The repository clone URL, kept for provenance.</param>
    /// <param name="parentId">Id of the node the repository attaches to (deepest group, host, or root).</param>
    /// <param name="contributors">The repository's commit authors with counts (most active first).</param>
    internal static IngestionItem BuildRepoItem(
        string slug, string? repositoryUrl, string parentId, IReadOnlyList<GitContributor> contributors)
    {
        var body = $"Git repository '{slug}' ingested into the knowledge graph.";
        if (contributors.Count > 0)
        {
            var listed = contributors.Take(MaxRepoContributors).Select(c => $"{c.Name} ({c.Commits})");
            body += $"\n\nBeitragende (nach Commits): {string.Join(", ", listed)}";
            body += contributors.Count > MaxRepoContributors
                ? $" … (+{contributors.Count - MaxRepoContributors} weitere)."
                : ".";
        }

        return new IngestionItem
        {
            Id = GitItemIdentity.RepoId(slug),
            Title = slug,
            Body = body,
            SourceKind = "git",
            SourceUrl = repositoryUrl,
            Domain = "git-knowledge",
            Tags = ["git-knowledge", slug.ToLowerInvariant()],
            NativeLinks = [new IngestionLink { Kind = "related", TargetRef = parentId }],
        };
    }

    /// <summary>
    /// Builds the structural hierarchy nodes (root, host, groups) derived from the canonical URL and
    /// returns them together with the id the repository node should attach to. A null/local URL yields
    /// only the shared root, so the repository attaches directly to <c>git-knowledge</c>.
    /// </summary>
    /// <param name="canonicalUrl">The canonical repository URL, or null/local.</param>
    internal static (IReadOnlyList<IngestionItem> Items, string RepoParentId) BuildHierarchy(string? canonicalUrl)
    {
        var items = new List<IngestionItem> { BuildRootItem() };
        var parentId = GitItemIdentity.RootId;

        if (string.IsNullOrWhiteSpace(canonicalUrl))
            return (items, parentId);

        var (host, ns, _) = GitItemIdentity.Parse(canonicalUrl);
        if (host is null)
            return (items, parentId);

        var hostId = GitItemIdentity.HostId(host);
        items.Add(BuildStructuralItem(hostId, host, parentId));
        parentId = hostId;

        var cumulative = string.Empty;
        foreach (var segment in ns)
        {
            cumulative = cumulative.Length == 0 ? segment : $"{cumulative}/{segment}";
            var groupId = GitItemIdentity.GroupId(cumulative);
            items.Add(BuildStructuralItem(groupId, segment, parentId));
            parentId = groupId;
        }

        return (items, parentId);
    }

    /// <summary>Builds a structural hierarchy node (host or group) linked to its parent.</summary>
    private static IngestionItem BuildStructuralItem(string id, string title, string parentId) => new()
    {
        Id = id,
        Title = title,
        Body = $"Structural node '{title}' in the Git knowledge hierarchy.",
        SourceKind = "git",
        Domain = "git-knowledge",
        Tags = ["git-knowledge"],
        NativeLinks = [new IngestionLink { Kind = "related", TargetRef = parentId }],
    };

    internal static string ToRelativePath(string root, string fullPath)
    {
        var normalizedRoot = root.Replace('\\', '/').TrimEnd('/');
        var normalizedFull = fullPath.Replace('\\', '/');

        if (normalizedRoot.Length > 0
            && normalizedFull.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFull[(normalizedRoot.Length + 1)..];
        }

        return normalizedFull.TrimStart('/');
    }

    internal static string FileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;

        if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            name = name[..^3];

        return name;
    }

    internal static string DirectoryOf(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[..slash] : string.Empty;
    }

    internal static string? ResolveLink(string slug, string fromRelativePath, string linkTarget)
    {
        // Only resolve repository-relative links; ignore external URLs and absolute paths.
        if (linkTarget.Contains("://", StringComparison.Ordinal) || linkTarget.StartsWith('/'))
            return null;

        var combined = CombineRelative(DirectoryOf(fromRelativePath), linkTarget);
        return combined is null ? null : GitItemIdentity.ItemId(slug, combined);
    }

    internal static string? CombineRelative(string baseDir, string link)
    {
        var segments = new List<string>();
        if (!string.IsNullOrEmpty(baseDir))
            segments.AddRange(baseDir.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in link.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    return null; // link escapes the repository root

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    internal static IReadOnlyList<string> PathTags(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var tags = new List<string>();
        for (var i = 0; i < segments.Length - 1; i++)
            tags.Add(segments[i].ToLowerInvariant());

        var fileName = segments.Length > 0 ? segments[^1] : normalized;
        if (fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase))
            tags.Add("readme");
        if (fileName.StartsWith("CONTRIBUTING", StringComparison.OrdinalIgnoreCase))
            tags.Add("contributing");
        if (normalized.StartsWith(".gitlab/", StringComparison.OrdinalIgnoreCase))
            tags.Add("gitlab");
        if (normalized.Contains("/adr/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("adr/", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("adr");
        }

        return tags.Distinct(StringComparer.Ordinal).ToList();
    }
}
