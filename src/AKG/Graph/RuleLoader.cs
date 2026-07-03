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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RuleLoader> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="RuleLoader"/>.
    /// </summary>
    /// <param name="fileSystem">File system abstraction for reading Markdown files.</param>
    /// <param name="cypher">Cypher executor for upserting rules into Neo4j.</param>
    /// <param name="timeProvider">Time source for temporal edge stamps (C9).</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public RuleLoader(IFileSystem fileSystem, ICypherExecutor cypher, TimeProvider timeProvider, ILogger<RuleLoader> logger)
    {
        _fileSystem = fileSystem;
        _parser = new KnowledgeRuleParser();
        _cypher = cypher;
        _timeProvider = timeProvider;
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
        // C9: same temporal replace query as Neo4jKnowledgeGraph.UpsertEdgesAsync (one batched
        // round-trip instead of the previous 1+N): open edges of this type whose target is no longer
        // declared are closed (validUntil = now) instead of deleted; declared targets are merged with
        // a first-seen validFrom (ON CREATE) and re-opened when previously closed. relType is a fixed
        // internal constant (never user input), so interpolating it into the query is safe.
        var now = _timeProvider.GetUtcNow().ToString("O");
        await _cypher.ExecuteAsync(
            "MATCH (s:Rule {id: $sourceId}) " +
            $"OPTIONAL MATCH (s)-[stale:{relType}]->(t0:Rule) " +
            "WHERE stale.validUntil IS NULL AND NOT t0.id IN $targetIds " +
            "SET stale.validUntil = $now " +
            "WITH DISTINCT s " +
            "UNWIND $targetIds AS targetId " +
            "MATCH (t:Rule {id: targetId}) " +
            $"MERGE (s)-[e:{relType}]->(t) " +
            "ON CREATE SET e.validFrom = $now " +
            "SET e.validUntil = null",
            new { sourceId, targetIds, now },
            ct).ConfigureAwait(false);
    }
}
