using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// <see cref="IRuleRecycleBin"/> over the graph (E10): lists soft-deleted rules and restores or
/// permanently purges them. Ownership mirrors the delete rules — non-admins only see and act on
/// their own rules. Restore and purge are audited (RuleRestored / RulePurged).
/// </summary>
internal sealed class RuleRecycleBin : IRuleRecycleBin
{
    private const int MaxPreviewChars = 120;

    private readonly ICypherExecutor _cypher;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<RuleRecycleBin> _logger;

    /// <summary>Initializes a new <see cref="RuleRecycleBin"/>.</summary>
    /// <param name="cypher">Cypher executor for graph access.</param>
    /// <param name="auditLog">Audit log for restore/purge events.</param>
    /// <param name="logger">Structured logger.</param>
    public RuleRecycleBin(ICypherExecutor cypher, IAuditLog auditLog, ILogger<RuleRecycleBin> logger)
    {
        _cypher = cypher;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeletedRuleInfo>> ListAsync(
        string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE r.deletedAt IS NOT NULL AND ($isAdmin OR r.ownerId = $userId)
            RETURN r
            """,
            new { userId, isAdmin },
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(row => NodeMapper.ExtractProperties(row.TryGetValue("r", out var r) ? r : null))
            .Where(props => props is not null)
            .Select(props => ToInfo(props!))
            .Where(info => info.Id.Length > 0)
            .OrderByDescending(info => info.DeletedAt)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(
        string ruleId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        var deleted = await FindDeletedAsync(ruleId, cancellationToken).ConfigureAwait(false);
        if (deleted is null) return false;
        EnsureOwnership(deleted, ruleId, userId, isAdmin, "restore");

        // The CASE only clears validUntil when it came from the delete — an earlier supersede
        // timestamp survives the restore.
        await _cypher.ExecuteAsync(
            """
            MATCH (r:Rule {id: $ruleId})
            WHERE r.deletedAt IS NOT NULL
            SET r.validUntil = CASE WHEN r.validUntil = r.deletedAt THEN null ELSE r.validUntil END,
                r.deletedAt = null,
                r.deletedBy = null
            """,
            new { ruleId },
            cancellationToken).ConfigureAwait(false);

        await _auditLog.LogAsync(
            AuditEvent.RuleRestored, userId, $"Rule '{ruleId}' restored from the recycle bin.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Restored rule '{RuleId}' by user '{UserId}' | {Component}", ruleId, userId, "AKG");
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> PurgeAsync(
        string ruleId, string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        var deleted = await FindDeletedAsync(ruleId, cancellationToken).ConfigureAwait(false);
        if (deleted is null) return false;
        EnsureOwnership(deleted, ruleId, userId, isAdmin, "purge");

        // The pre-E10 hard delete: remove the rule and its chunks for good.
        await _cypher.ExecuteAsync(
            """
            MATCH (r:Rule {id: $ruleId})
            OPTIONAL MATCH (r)-[:HAS_CHUNK]->(c:RuleChunk)
            DETACH DELETE r, c
            """,
            new { ruleId },
            cancellationToken).ConfigureAwait(false);

        await _auditLog.LogAsync(
            AuditEvent.RulePurged, userId, $"Rule '{ruleId}' permanently purged from the recycle bin.",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Purged rule '{RuleId}' by user '{UserId}' | {Component}", ruleId, userId, "AKG");
        return true;
    }

    private async Task<IReadOnlyDictionary<string, object?>?> FindDeletedAsync(
        string ruleId, CancellationToken ct)
    {
        var rows = await _cypher.QueryAsync(
            "MATCH (r:Rule {id: $ruleId}) WHERE r.deletedAt IS NOT NULL RETURN r",
            new { ruleId },
            ct).ConfigureAwait(false);
        return rows.Count == 0
            ? null
            : NodeMapper.ExtractProperties(rows[0].TryGetValue("r", out var r) ? r : null);
    }

    private static void EnsureOwnership(
        IReadOnlyDictionary<string, object?> props, string ruleId, string userId, bool isAdmin, string action)
    {
        var ownerId = props.TryGetValue("ownerId", out var o) ? o?.ToString() : null;
        if (!isAdmin && ownerId != userId)
            throw new UnauthorizedAccessException(
                $"User '{userId}' is not authorized to {action} rule '{ruleId}'.");
    }

    private static DeletedRuleInfo ToInfo(IReadOnlyDictionary<string, object?> props) => new()
    {
        Id = props.TryGetValue("id", out var id) ? id?.ToString() ?? "" : "",
        BodyPreview = Preview(props.TryGetValue("body", out var b) ? b?.ToString() : null),
        Domain = props.TryGetValue("domain", out var d) ? d?.ToString() ?? "general" : "general",
        OwnerId = props.TryGetValue("ownerId", out var o) ? o?.ToString() : null,
        DeletedAt = NodeMapper.ParseTimestamp(props.TryGetValue("deletedAt", out var da) ? da : null),
        DeletedBy = props.TryGetValue("deletedBy", out var db) ? db?.ToString() : null,
    };

    private static string Preview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var newline = body.IndexOfAny(['\n', '\r']);
        var line = (newline >= 0 ? body[..newline] : body).Trim().TrimStart('#').Trim();
        return line.Length <= MaxPreviewChars ? line : line[..MaxPreviewChars] + "…";
    }
}
