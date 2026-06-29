using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// Graph-backed implementation of <see cref="IDomainManager"/>.
/// Manages the domain hierarchy (Domain nodes and HAS_SUBDOMAIN edges) in the AKG.
/// Works with any Cypher-compatible graph database via <see cref="ICypherExecutor"/>.
/// </summary>
internal sealed class DomainManager : IDomainManager
{
    private readonly ICypherExecutor _cypher;
    private readonly ILogger<DomainManager> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DomainManager"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor for all graph operations.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    internal DomainManager(ICypherExecutor cypher, ILogger<DomainManager> logger)
    {
        _cypher = cypher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DomainNode>> GetDomainTreeAsync(CancellationToken ct = default)
    {
        var rows = await _cypher.QueryAsync(
            """
            MATCH (d:Domain)
            OPTIONAL MATCH (d)<-[:HAS_SUBDOMAIN]-(parent:Domain)
            RETURN d, parent.name AS parentName
            """,
            ct: ct).ConfigureAwait(false);

        var domains = new Dictionary<string, (string Name, string Label, string? Description, string? Parent, bool IsCore)>(
            StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!row.TryGetValue("d", out var dObj)) continue;
            var props = NodeMapper.ExtractProperties(dObj);
            if (props is null) continue;

            var name = props.TryGetValue("name", out var n) ? n?.ToString() ?? string.Empty : string.Empty;
            if (string.IsNullOrEmpty(name)) continue;

            var label = props.TryGetValue("label", out var l) ? l?.ToString() ?? name : name;
            var description = props.TryGetValue("description", out var desc) ? desc?.ToString() : null;
            var isCore = props.TryGetValue("isCore", out var ic) && ic is bool b && b;
            var parent = row.TryGetValue("parentName", out var pn) ? pn?.ToString() : null;

            domains[name] = (name, label, description, parent, isCore);
        }

        // Compute sub-domain lists
        var subdomains = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, (name, _, _, parent, _)) in domains)
        {
            if (parent is null) continue;
            if (!subdomains.TryGetValue(parent, out var list))
            {
                list = [];
                subdomains[parent] = list;
            }

            list.Add(name);
        }

        return domains.Values
            .Select(d => new DomainNode(
                d.Name,
                d.Label,
                d.Description,
                d.Parent,
                subdomains.TryGetValue(d.Name, out var subs) ? subs : (IReadOnlyList<string>)[],
                d.IsCore))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<DomainNode> CreateDomainAsync(
        string name,
        string label,
        string? parentDomain,
        string? description,
        string? ownerId,
        CancellationToken ct = default)
    {
        await _cypher.ExecuteAsync(
            """
            MERGE (d:Domain {name: $name})
            SET d.label = $label,
                d.description = $description,
                d.ownerId = $ownerId,
                d.isCore = false
            WITH d
            OPTIONAL MATCH (parent:Domain {name: $parentName})
            WITH d, parent
            FOREACH (_ IN CASE WHEN parent IS NOT NULL THEN [1] ELSE [] END |
                MERGE (parent)-[:HAS_SUBDOMAIN]->(d))
            """,
            new { name, label, description, ownerId, parentName = parentDomain },
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Created domain '{Name}' (parent: {Parent}, owner: {Owner}) | {Component}",
            name, parentDomain ?? "none", ownerId ?? "system", "AKG");

        return new DomainNode(name, label, description, parentDomain, [], IsCore: false);
    }

    /// <inheritdoc/>
    public async Task DeleteDomainAsync(string name, CancellationToken ct = default)
    {
        await _cypher.ExecuteAsync(
            "MATCH (d:Domain {name: $name}) DETACH DELETE d",
            new { name },
            ct).ConfigureAwait(false);

        _logger.LogInformation("Deleted domain '{Name}' | {Component}", name, "AKG");
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string name, CancellationToken ct = default)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (d:Domain {name: $name}) RETURN count(d) AS cnt",
            new { name },
            ct).ConfigureAwait(false);

        if (rows.Count == 0) return false;
        var val = rows[0].TryGetValue("cnt", out var v) ? v : null;
        return Convert.ToInt64(val ?? 0L) > 0;
    }
}
