using Edda.AKG.Parser;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// Seeds <c>:WorldKnowledge</c> nodes in Neo4j from Markdown files on disk.
/// Used by <see cref="WorldKnowledgeSeedHostedService"/> on startup to populate
/// the world knowledge base that <c>WorldKnowledgeFetcher</c> queries during
/// context compilation (Phase 4).
/// </summary>
internal sealed class WorldKnowledgeSeeder : IWorldKnowledgeSeeder
{
    private readonly IFileSystem _fileSystem;
    private readonly ICypherExecutor _cypher;
    private readonly KnowledgeRuleParser _parser;
    private readonly ILogger<WorldKnowledgeSeeder> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="WorldKnowledgeSeeder"/>.
    /// </summary>
    /// <param name="fileSystem">File system abstraction for reading Markdown seed files.</param>
    /// <param name="cypher">Cypher executor for upserting nodes into Neo4j.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public WorldKnowledgeSeeder(
        IFileSystem fileSystem,
        ICypherExecutor cypher,
        ILogger<WorldKnowledgeSeeder> logger)
    {
        _fileSystem = fileSystem;
        _cypher = cypher;
        _parser = new KnowledgeRuleParser();
        _logger = logger;
    }

    /// <summary>
    /// Returns the current count of <c>:WorldKnowledge</c> nodes in Neo4j.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of <c>:WorldKnowledge</c> nodes currently in the graph.</returns>
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (w:WorldKnowledge) RETURN count(w) AS n",
            ct: ct).ConfigureAwait(false);

        if (rows.Count == 0) return 0L;
        return Convert.ToInt64(rows[0].TryGetValue("n", out var n) ? n : 0L);
    }

    /// <summary>
    /// Reads all <c>*.md</c> files from the given directory, parses each as a
    /// <see cref="Core.Models.KnowledgeRule"/>, and upserts it as a <c>:WorldKnowledge</c>
    /// node. Invalid files are skipped with a warning.
    /// </summary>
    /// <param name="directory">Path to the world knowledge seed directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of nodes successfully seeded.</returns>
    public async Task<int> SeedFromDirectoryAsync(string directory, CancellationToken ct = default)
    {
        if (!_fileSystem.DirectoryExists(directory))
        {
            _logger.LogWarning(
                "World knowledge directory '{Directory}' does not exist; skipping seed | {Component}",
                directory, "AKG");
            return 0;
        }

        var files = _fileSystem.EnumerateFiles(directory, "*.md", recursive: true).ToList();
        var seeded = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var content = await _fileSystem.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var rule = _parser.Parse(content);

                await _cypher.ExecuteAsync(
                    """
                    MERGE (w:WorldKnowledge {id: $id})
                    SET w.type = $type,
                        w.domain = $domain,
                        w.priority = $priority,
                        w.body = $body,
                        w.tags = $tags
                    """,
                    new
                    {
                        id = rule.Id,
                        type = rule.Type,
                        domain = rule.Domain,
                        priority = rule.Priority.ToString(),
                        body = rule.Body,
                        tags = rule.Tags.ToArray(),
                    },
                    ct).ConfigureAwait(false);

                seeded++;
            }
            catch (RuleParseException ex)
            {
                _logger.LogWarning(ex,
                    "Skipping world knowledge file '{File}': missing required frontmatter field '{Field}' | {Component}",
                    file, ex.MissingField, "AKG");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to seed world knowledge from '{File}' | {Component}",
                    file, "AKG");
            }
        }

        _logger.LogInformation(
            "World knowledge seed complete: {Seeded}/{Total} nodes from '{Directory}' | {Component}",
            seeded, files.Count, directory, "AKG");

        return seeded;
    }

    /// <summary>
    /// Seeds from the given directory only when the graph is empty.
    /// If <c>:WorldKnowledge</c> nodes already exist, the operation is skipped.
    /// </summary>
    /// <param name="directory">Path to the world knowledge seed directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of nodes seeded, or zero if the graph was already populated.</returns>
    public async Task<int> SeedIfEmptyAsync(string directory, CancellationToken ct = default)
    {
        var count = await CountAsync(ct).ConfigureAwait(false);
        if (count > 0)
        {
            _logger.LogDebug(
                "WorldKnowledge already seeded ({Count} nodes), skipping | {Component}",
                count, "AKG");
            return 0;
        }

        return await SeedFromDirectoryAsync(directory, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes all existing <c>:WorldKnowledge</c> nodes and re-seeds from the given directory.
    /// </summary>
    /// <param name="directory">Path to the world knowledge seed directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of nodes successfully loaded after the reload.</returns>
    public async Task<int> ReloadAsync(string directory, CancellationToken ct = default)
    {
        await _cypher.ExecuteAsync(
            "MATCH (w:WorldKnowledge) DETACH DELETE w",
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation("Deleted all existing WorldKnowledge nodes | {Component}", "AKG");

        return await SeedFromDirectoryAsync(directory, ct).ConfigureAwait(false);
    }
}
