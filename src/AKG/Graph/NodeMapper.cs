using System.Globalization;
using Edda.Core.Models;
using Neo4j.Driver;

namespace Edda.AKG.Graph;

/// <summary>
/// Provides helper methods for mapping graph node objects to Core model types.
/// Supports both Neo4j <see cref="INode"/> instances and plain dictionary representations,
/// allowing alternative Cypher-compatible graph databases (Memgraph, FalkorDB) to work transparently.
/// </summary>
internal static class NodeMapper
{
    /// <summary>
    /// Extracts properties from a graph node object, regardless of the underlying driver type.
    /// Returns a Properties-compatible dictionary for INode,
    /// or the dictionary itself for plain dictionary representations.
    /// </summary>
    /// <param name="nodeObj">The raw node object from a Cypher query result.</param>
    /// <returns>A read-only dictionary of property names to values, or <see langword="null"/> if the object type is not recognized.</returns>
    internal static IReadOnlyDictionary<string, object?>? ExtractProperties(object? nodeObj)
    {
        if (nodeObj is INode node)
            return node.Properties;
        if (nodeObj is IReadOnlyDictionary<string, object?> dict)
            return dict;
        if (nodeObj is IDictionary<string, object?> mutableDict)
            return mutableDict.AsReadOnly();
        return null;
    }

    /// <summary>
    /// Maps a Neo4j <see cref="INode"/> to a <see cref="KnowledgeRule"/>.
    /// </summary>
    /// <param name="node">The Neo4j node to map.</param>
    /// <returns>A populated <see cref="KnowledgeRule"/> instance.</returns>
    internal static KnowledgeRule MapNode(INode node)
    {
        return new KnowledgeRule
        {
            Id = node.Properties.TryGetValue("id", out var idVal) ? idVal?.ToString() ?? string.Empty : string.Empty,
            Type = node.Properties.TryGetValue("type", out var t) ? t?.ToString() ?? "Rule" : "Rule",
            Domain = node.Properties.TryGetValue("domain", out var d) ? d?.ToString() ?? "general" : "general",
            Priority = ParsePriority(node.Properties.TryGetValue("priority", out var p) ? p?.ToString() : null),
            Body = node.Properties.TryGetValue("body", out var b) ? b?.ToString() ?? string.Empty : string.Empty,
            Tags = node.Properties.TryGetValue("tags", out var tags) ? MapStringList(tags) : [],
            OwnerId = node.Properties.TryGetValue("ownerId", out var o) ? o?.ToString() : null,
            TenantId = node.Properties.TryGetValue("tenantId", out var tn) ? tn?.ToString() ?? Tenants.DefaultTenantId : Tenants.DefaultTenantId,
            RelatesTo = MapRelationsFromNode(node),
            ValidFrom = ParseTimestamp(node.Properties.TryGetValue("validFrom", out var vf) ? vf : null),
            ValidUntil = ParseTimestamp(node.Properties.TryGetValue("validUntil", out var vu) ? vu : null),
            InvalidatedBy = node.Properties.TryGetValue("invalidatedBy", out var ib) ? ib?.ToString() : null,
        };
    }

    /// <summary>
    /// Maps a raw query result object to a <see cref="KnowledgeRule"/>.
    /// Supports both <see cref="INode"/> and plain dictionary representations.
    /// </summary>
    /// <param name="nodeObj">The raw node object from the query result.</param>
    /// <returns>A populated <see cref="KnowledgeRule"/>, or a minimal stub if the object is not a valid node.</returns>
    internal static KnowledgeRule MapRowObject(object? nodeObj)
    {
        if (nodeObj is INode node)
            return MapNode(node);

        // Fallback for plain dictionary representation (alternative graph DB drivers)
        if (nodeObj is IReadOnlyDictionary<string, object?> dict)
            return MapDictionary(dict);

        if (nodeObj is IDictionary<string, object?> mutableDict)
            return MapDictionary(mutableDict.AsReadOnly());

        return new KnowledgeRule
        {
            Id = "unknown",
            Type = "Rule",
            Domain = "general",
            Priority = RulePriority.Medium,
            Body = string.Empty,
        };
    }

    /// <summary>
    /// Maps a raw query result object to a <see cref="KnowledgeRule"/>.
    /// Supports both Neo4j <see cref="INode"/> and plain dictionary representations.
    /// Returns <see langword="null"/> if the object cannot be mapped or has no valid ID.
    /// </summary>
    /// <param name="nodeObj">The raw node object from the query result.</param>
    /// <returns>A populated <see cref="KnowledgeRule"/>, or <see langword="null"/> if invalid.</returns>
    internal static KnowledgeRule? MapKnowledgeRow(object? nodeObj)
    {
        var props = ExtractProperties(nodeObj);
        if (props is null) return null;

        var id = props.TryGetValue("id", out var idVal) ? idVal?.ToString() : null;
        if (string.IsNullOrEmpty(id)) return null;

        return new KnowledgeRule
        {
            Id = id,
            Type = props.TryGetValue("type", out var t) ? t?.ToString() ?? "Rule" : "Rule",
            Domain = props.TryGetValue("domain", out var d) ? d?.ToString() ?? "general" : "general",
            Priority = ParsePriority(props.TryGetValue("priority", out var p) ? p?.ToString() : null),
            Body = props.TryGetValue("body", out var b) ? b?.ToString() ?? string.Empty : string.Empty,
            Tags = props.TryGetValue("tags", out var tags) ? MapStringList(tags) : [],
            OwnerId = props.TryGetValue("ownerId", out var o) ? o?.ToString() : null,
            TenantId = props.TryGetValue("tenantId", out var tn) ? tn?.ToString() ?? Tenants.DefaultTenantId : Tenants.DefaultTenantId,
            RelatesTo = MapRelationsFromDictionary(props),
            ValidFrom = ParseTimestamp(props.TryGetValue("validFrom", out var vf) ? vf : null),
            ValidUntil = ParseTimestamp(props.TryGetValue("validUntil", out var vu) ? vu : null),
            InvalidatedBy = props.TryGetValue("invalidatedBy", out var ib) ? ib?.ToString() : null,
        };
    }

    private static KnowledgeRule MapDictionary(IReadOnlyDictionary<string, object?> dict)
    {
        return new KnowledgeRule
        {
            Id = dict.TryGetValue("id", out var id) ? id?.ToString() ?? "unknown" : "unknown",
            Type = dict.TryGetValue("type", out var t) ? t?.ToString() ?? "Rule" : "Rule",
            Domain = dict.TryGetValue("domain", out var d) ? d?.ToString() ?? "general" : "general",
            Priority = ParsePriority(dict.TryGetValue("priority", out var p) ? p?.ToString() : null),
            Body = dict.TryGetValue("body", out var b) ? b?.ToString() ?? string.Empty : string.Empty,
            Tags = dict.TryGetValue("tags", out var tags) ? MapStringList(tags) : [],
            OwnerId = dict.TryGetValue("ownerId", out var o) ? o?.ToString() : null,
            TenantId = dict.TryGetValue("tenantId", out var tn) ? tn?.ToString() ?? Tenants.DefaultTenantId : Tenants.DefaultTenantId,
            RelatesTo = MapRelationsFromDictionary(dict),
            ValidFrom = ParseTimestamp(dict.TryGetValue("validFrom", out var vf) ? vf : null),
            ValidUntil = ParseTimestamp(dict.TryGetValue("validUntil", out var vu) ? vu : null),
            InvalidatedBy = dict.TryGetValue("invalidatedBy", out var ib) ? ib?.ToString() : null,
        };
    }

    private static RuleRelations? MapRelationsFromDictionary(IReadOnlyDictionary<string, object?> dict)
    {
        var implies = dict.TryGetValue("implies", out var imp) ? MapStringList(imp) : null;
        var conflicts = dict.TryGetValue("conflictsWith", out var cf) ? MapStringList(cf) : null;
        var exceptions = dict.TryGetValue("exceptionFor", out var ef) ? MapStringList(ef) : null;
        var requires = dict.TryGetValue("requires", out var rq) ? MapStringList(rq) : null;
        var supersedes = dict.TryGetValue("supersedes", out var ss) ? MapStringList(ss) : null;
        var related = dict.TryGetValue("related", out var rel) ? MapStringList(rel) : null;

        if (implies is null && conflicts is null && exceptions is null && requires is null && supersedes is null && related is null)
            return null;

        return new RuleRelations
        {
            Implies = implies ?? [],
            ConflictsWith = conflicts ?? [],
            ExceptionFor = exceptions ?? [],
            Requires = requires ?? [],
            Supersedes = supersedes ?? [],
            Related = related ?? [],
        };
    }

    private static RuleRelations? MapRelationsFromNode(INode node)
    {
        var implies = node.Properties.TryGetValue("implies", out var imp) ? MapStringList(imp) : null;
        var conflicts = node.Properties.TryGetValue("conflictsWith", out var cf) ? MapStringList(cf) : null;
        var exceptions = node.Properties.TryGetValue("exceptionFor", out var ef) ? MapStringList(ef) : null;
        var requires = node.Properties.TryGetValue("requires", out var rq) ? MapStringList(rq) : null;
        var supersedes = node.Properties.TryGetValue("supersedes", out var ss) ? MapStringList(ss) : null;
        var related = node.Properties.TryGetValue("related", out var rel) ? MapStringList(rel) : null;

        if (implies is null && conflicts is null && exceptions is null && requires is null && supersedes is null && related is null)
            return null;

        return new RuleRelations
        {
            Implies = implies ?? [],
            ConflictsWith = conflicts ?? [],
            ExceptionFor = exceptions ?? [],
            Requires = requires ?? [],
            Supersedes = supersedes ?? [],
            Related = related ?? [],
        };
    }

    internal static IReadOnlyList<string> MapStringList(object? value)
    {
        if (value is IEnumerable<object> list)
            return list.Select(x => x?.ToString() ?? string.Empty).Where(s => s != string.Empty).ToList();
        if (value is string s && !string.IsNullOrWhiteSpace(s))
            return [s];
        return [];
    }

    internal static RulePriority ParsePriority(string? value)
    {
        return value switch
        {
            "Critical" => RulePriority.Critical,
            "High" => RulePriority.High,
            "Low" => RulePriority.Low,
            _ => RulePriority.Medium,
        };
    }

    /// <summary>
    /// Parses an ISO-8601 timestamp property into a <see cref="DateTimeOffset"/>, or null when the
    /// value is missing or unparseable.
    /// </summary>
    /// <param name="value">The raw property value (expected to be an ISO-8601 string).</param>
    /// <returns>The parsed timestamp, or <see langword="null"/>.</returns>
    internal static DateTimeOffset? ParseTimestamp(object? value)
        => value is string s
           && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : null;
}
