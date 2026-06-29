using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// Cypher-backed <see cref="IEntityStore"/>. Stores the LightRAG-style entity layer as
/// <c>(:Entity)</c> nodes keyed by <c>(ownerId, normalizedName)</c> and <c>[:RELATES_TO]</c> edges.
/// Schema (constraint + index) is bootstrapped lazily on first ingest.
/// </summary>
public sealed class Neo4jEntityStore : IEntityStore
{
    private readonly ICypherExecutor _cypher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<Neo4jEntityStore> _logger;
    private bool _schemaEnsured;

    /// <summary>Initializes a new <see cref="Neo4jEntityStore"/>.</summary>
    /// <param name="cypher">Cypher executor for graph reads/writes.</param>
    /// <param name="timeProvider">Time source for created/updated timestamps (Regel 4).</param>
    /// <param name="logger">Logger.</param>
    public Neo4jEntityStore(
        ICypherExecutor cypher, TimeProvider timeProvider, ILogger<Neo4jEntityStore> logger)
    {
        _cypher = cypher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<EntityIngestionResult> IngestAsync(
        EntityExtractionResult extraction, string userId, string sourceType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (extraction.Entities.Count == 0 && extraction.Relations.Count == 0)
        {
            return EntityIngestionResult.Empty;
        }

        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow().ToString("o");

        var entityRows = extraction.Entities
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e =>
            {
                var norm = Normalize(e.Name);
                return new Dictionary<string, object?>
                {
                    ["id"] = $"entity-{norm}",
                    ["name"] = e.Name.Trim(),
                    ["normalizedName"] = norm,
                    ["type"] = string.IsNullOrWhiteSpace(e.Type) ? "concept" : e.Type.Trim(),
                    ["description"] = e.Description?.Trim() ?? "",
                };
            })
            .GroupBy(r => (string)r["normalizedName"]!)
            .Select(g => g.First())
            .ToList();

        var entitiesIngested = 0;
        if (entityRows.Count > 0)
        {
            await _cypher.ExecuteAsync(
                """
                UNWIND $entities AS ent
                MERGE (e:Entity {ownerId: $ownerId, normalizedName: ent.normalizedName})
                ON CREATE SET e.id = ent.id, e.name = ent.name, e.type = ent.type,
                    e.description = ent.description, e.sourceType = $sourceType,
                    e.mentions = 1, e.createdAt = $now, e.updatedAt = $now
                ON MATCH SET e.mentions = coalesce(e.mentions, 0) + 1, e.updatedAt = $now,
                    e.description = CASE WHEN size(ent.description) > size(coalesce(e.description, ''))
                        THEN ent.description ELSE e.description END
                """,
                new { entities = entityRows, ownerId = userId, sourceType, now },
                cancellationToken).ConfigureAwait(false);
            entitiesIngested = entityRows.Count;
        }

        var relationRows = extraction.Relations
            .Where(r => !string.IsNullOrWhiteSpace(r.Source) && !string.IsNullOrWhiteSpace(r.Target))
            .Select(r => new Dictionary<string, object?>
            {
                ["sourceNorm"] = Normalize(r.Source),
                ["targetNorm"] = Normalize(r.Target),
                ["description"] = r.Description?.Trim() ?? "",
                ["keywords"] = r.Keywords.ToList(),
            })
            .Where(r => !string.Equals((string)r["sourceNorm"]!, (string)r["targetNorm"]!, StringComparison.Ordinal))
            .ToList();

        var relationsIngested = 0;
        if (relationRows.Count > 0)
        {
            await _cypher.ExecuteAsync(
                """
                UNWIND $relations AS rel
                MATCH (s:Entity {ownerId: $ownerId, normalizedName: rel.sourceNorm})
                MATCH (t:Entity {ownerId: $ownerId, normalizedName: rel.targetNorm})
                MERGE (s)-[r:RELATES_TO]->(t)
                ON CREATE SET r.description = rel.description, r.keywords = rel.keywords,
                    r.weight = 1, r.createdAt = $now, r.updatedAt = $now
                ON MATCH SET r.weight = coalesce(r.weight, 0) + 1, r.updatedAt = $now
                """,
                new { relations = relationRows, ownerId = userId, now },
                cancellationToken).ConfigureAwait(false);
            relationsIngested = relationRows.Count;
        }

        _logger.LogInformation(
            "Entity ingest: {Entities} entit(ies), {Relations} relation(s) for user={User} source={Source} | {Component}",
            entitiesIngested, relationsIngested, userId, sourceType, "AKG");

        return new EntityIngestionResult
        {
            EntitiesIngested = entitiesIngested,
            RelationsIngested = relationsIngested,
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphEntity>> FindEntitiesAsync(
        IReadOnlyList<string> terms, string? userId, int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var normTerms = terms
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (normTerms.Count == 0)
        {
            return [];
        }

        var rows = await _cypher.QueryAsync(
            """
            UNWIND $terms AS term
            MATCH (e:Entity)
            WHERE ($userId IS NULL OR e.ownerId = $userId) AND toLower(e.name) CONTAINS term
            RETURN DISTINCT e.name AS name, e.type AS type, e.description AS description,
                coalesce(e.mentions, 0) AS mentions
            LIMIT $limit
            """,
            new { terms = normTerms, userId, limit },
            cancellationToken).ConfigureAwait(false);

        return MapEntities(rows);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GraphEntity>> GetRelatedAsync(
        string entityName, string? userId, int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            return [];
        }

        var rows = await _cypher.QueryAsync(
            """
            MATCH (e:Entity {normalizedName: $nname})-[:RELATES_TO]-(other:Entity)
            WHERE ($userId IS NULL OR e.ownerId = $userId)
            RETURN DISTINCT other.name AS name, other.type AS type, other.description AS description,
                coalesce(other.mentions, 0) AS mentions
            LIMIT $limit
            """,
            new { nname = Normalize(entityName), userId, limit },
            cancellationToken).ConfigureAwait(false);

        return MapEntities(rows);
    }

    /// <summary>Maps scalar-projected result rows to <see cref="GraphEntity"/> instances.</summary>
    private static IReadOnlyList<GraphEntity> MapEntities(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var result = new List<GraphEntity>(rows.Count);
        foreach (var row in rows)
        {
            var name = row.TryGetValue("name", out var n) ? n as string : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            result.Add(new GraphEntity
            {
                Name = name,
                Type = (row.TryGetValue("type", out var t) ? t as string : null) ?? "concept",
                Description = (row.TryGetValue("description", out var d) ? d as string : null) ?? "",
                Mentions = row.TryGetValue("mentions", out var m) && m is not null ? Convert.ToInt32(m) : 0,
            });
        }
        return result;
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Best-effort one-time schema bootstrap (constraint + index). Failures are logged, not thrown.</summary>
    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaEnsured)
        {
            return;
        }

        try
        {
            await _cypher.ExecuteAsync(
                "CREATE CONSTRAINT entity_owner_name_unique IF NOT EXISTS " +
                "FOR (e:Entity) REQUIRE (e.ownerId, e.normalizedName) IS UNIQUE",
                null, cancellationToken).ConfigureAwait(false);
            await _cypher.ExecuteAsync(
                "CREATE INDEX entity_name_index IF NOT EXISTS FOR (e:Entity) ON (e.name)",
                null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Entity schema bootstrap failed (non-fatal) | {Component}", "AKG");
        }

        _schemaEnsured = true;
    }
}
