using Edda.AKG.Parser;
using Edda.Core.Abstractions;
using Edda.Core.Exceptions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// Loads knowledge rules from Markdown files on disk and upserts them into Neo4j.
/// Uses <see cref="IFileSystem"/> for all file access and <see cref="KnowledgeRuleParser"/>
/// for parsing. Invalid files are skipped with a warning.
/// </summary>
internal sealed class RuleLoader : IRuleLoader
{
    private readonly IFileSystem _fileSystem;
    private readonly KnowledgeRuleParser _parser;
    private readonly ICypherExecutor _cypher;
    private readonly ILogger<RuleLoader> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="RuleLoader"/>.
    /// </summary>
    /// <param name="fileSystem">File system abstraction for reading Markdown files.</param>
    /// <param name="cypher">Cypher executor for upserting rules into Neo4j.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public RuleLoader(IFileSystem fileSystem, ICypherExecutor cypher, ILogger<RuleLoader> logger)
    {
        _fileSystem = fileSystem;
        _parser = new KnowledgeRuleParser();
        _cypher = cypher;
        _logger = logger;
    }

    /// <summary>
    /// Recursively loads all <c>*.md</c> files from the specified directory,
    /// parses each as a <see cref="KnowledgeRule"/>, and upserts it into Neo4j.
    /// </summary>
    /// <param name="directory">Root directory to search for Markdown rule files.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of rules successfully loaded and upserted.</returns>
    public async Task<int> LoadFromDirectoryAsync(string directory, CancellationToken ct)
    {
        if (!_fileSystem.DirectoryExists(directory))
        {
            _logger.LogWarning("Knowledge directory '{Directory}' does not exist; skipping rule load", directory);
            return 0;
        }

        var files = _fileSystem.EnumerateFiles(directory, "*.md", recursive: true).ToList();
        var loaded = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var content = await _fileSystem.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                var rule = _parser.Parse(content);
                await UpsertRuleAsync(rule, ct).ConfigureAwait(false);
                loaded++;
            }
            catch (RuleParseException ex)
            {
                _logger.LogWarning(ex, "Skipping '{File}': missing required frontmatter field '{Field}'", file, ex.MissingField);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to load rule from '{File}'", file);
            }
        }

        _logger.LogInformation(
            "Rule load complete: {Loaded}/{Total} rules from '{Directory}' | {Component}",
            loaded, files.Count, directory, "AKG");

        return loaded;
    }

    private async Task UpsertRuleAsync(KnowledgeRule rule, CancellationToken ct)
    {
        var implies = rule.RelatesTo?.Implies.ToArray() ?? [];
        var conflictsWith = rule.RelatesTo?.ConflictsWith.ToArray() ?? [];
        var exceptionFor = rule.RelatesTo?.ExceptionFor.ToArray() ?? [];
        var requires = rule.RelatesTo?.Requires.ToArray() ?? [];
        var supersedes = rule.RelatesTo?.Supersedes.ToArray() ?? [];
        var related = rule.RelatesTo?.Related.ToArray() ?? [];

        await _cypher.ExecuteAsync(
            """
            MERGE (r:Rule {id: $id})
            SET r.type = $type,
                r.domain = $domain,
                r.priority = $priority,
                r.body = $body,
                r.tags = $tags,
                r.ownerId = $ownerId,
                r.implies = $implies,
                r.conflictsWith = $conflictsWith,
                r.exceptionFor = $exceptionFor,
                r.requires = $requires,
                r.supersedes = $supersedes,
                r.related = $related,
                r.validatorScript = $validatorScript,
                r.validatorEnabled = $validatorEnabled,
                r.validatorHash = $validatorHash
            """,
            new
            {
                id = rule.Id,
                type = rule.Type,
                domain = rule.Domain,
                priority = rule.Priority.ToString(),
                body = rule.Body,
                tags = rule.Tags.ToArray(),
                ownerId = rule.OwnerId,
                implies,
                conflictsWith,
                exceptionFor,
                requires,
                supersedes,
                related,
                validatorScript = rule.ValidatorScript,
                validatorEnabled = rule.ValidatorEnabled,
                // F7: persist the script hash for confidence-history traceability (recomputed on each load).
                validatorHash = ValidatorScriptHash.Compute(rule.ValidatorScript),
            },
            ct).ConfigureAwait(false);

        // Create relationship edges for graph traversal
        await UpsertEdgesAsync(rule.Id, "IMPLIES", implies, ct).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "CONFLICTS_WITH", conflictsWith, ct).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "EXCEPTION_FOR", exceptionFor, ct).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "REQUIRES", requires, ct).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "SUPERSEDES", supersedes, ct).ConfigureAwait(false);
        await UpsertEdgesAsync(rule.Id, "RELATED", related, ct).ConfigureAwait(false);
    }

    private async Task UpsertEdgesAsync(string sourceId, string relType, string[] targetIds, CancellationToken ct)
    {
        if (targetIds.Length == 0) return;

        // Remove stale edges of this type from source, then create current ones
        await _cypher.ExecuteAsync(
            $"MATCH (s:Rule {{id: $sourceId}})-[e:{relType}]->() DELETE e",
            new { sourceId },
            ct).ConfigureAwait(false);

        foreach (var targetId in targetIds)
        {
            await _cypher.ExecuteAsync(
                $"MATCH (s:Rule {{id: $sourceId}}), (t:Rule {{id: $targetId}}) MERGE (s)-[:{relType}]->(t)",
                new { sourceId, targetId },
                ct).ConfigureAwait(false);
        }
    }
}
