using Edda.AKG.Authorization;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// <see cref="IRuleRecycleBin"/> over the graph (E10): lists soft-deleted rules and restores or
/// permanently purges them. Mutations go through the central role matrix (C2) — Editors act on
/// their own rules, restoring or purging foreign ones needs the Owner role (admins override).
/// Restore and purge are audited (RuleRestored / RulePurged).
/// </summary>
internal sealed class RuleRecycleBin : IRuleRecycleBin
{
    private const int MaxPreviewChars = 120;

    private readonly ICypherExecutor _cypher;
    private readonly IAuditLog _auditLog;
    private readonly ILogger<RuleRecycleBin> _logger;
    private readonly IIdentityContext? _identity;
    private readonly IRuleAuthorizer _authorizer;
    private readonly IDatasetWriteAuthorizer _writeAuthorizer;

    /// <summary>Initializes a new <see cref="RuleRecycleBin"/>.</summary>
    /// <param name="cypher">Cypher executor for graph access.</param>
    /// <param name="auditLog">Audit log for restore/purge events.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="identity">C1: ambient tenant source; null = default tenant.</param>
    /// <param name="authorizer">
    /// C2: central role enforcement for restore/purge. Null falls back to an internal
    /// <see cref="RuleAuthorizer"/> over <paramref name="identity"/> — without an identity that is
    /// the legacy owner/admin check.
    /// </param>
    /// <param name="writeAuthorizer">
    /// ADR-0014: dataset-aware write gate; null falls back to a pass-through over
    /// <paramref name="authorizer"/>, keeping the pre-dataset C2 behaviour.
    /// </param>
    public RuleRecycleBin(
        ICypherExecutor cypher, IAuditLog auditLog, ILogger<RuleRecycleBin> logger,
        IIdentityContext? identity = null, IRuleAuthorizer? authorizer = null,
        IDatasetWriteAuthorizer? writeAuthorizer = null)
    {
        _cypher = cypher;
        _auditLog = auditLog;
        _logger = logger;
        _identity = identity;
        _authorizer = authorizer ?? new RuleAuthorizer(identity);
        // ADR-0014 Slice 2b: dataset-aware write gate; the pass-through keeps the pre-dataset C2 behaviour.
        _writeAuthorizer = writeAuthorizer ?? new PassThroughDatasetWriteAuthorizer(_authorizer);
    }

    /// <summary>C1: the ambient tenant of the current context (read per call, never cached).</summary>
    private string Tenant => _identity?.TenantId ?? Tenants.DefaultTenantId;

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeletedRuleInfo>> ListAsync(
        string userId, bool isAdmin = false, CancellationToken cancellationToken = default)
    {
        var rows = await _cypher.QueryAsync(
            """
            MATCH (r:Rule)
            WHERE r.deletedAt IS NOT NULL AND ($isAdmin OR r.ownerId = $userId)
              AND coalesce(r.tenantId, 'default') = $tenantId
            RETURN r
            """,
            new { userId, isAdmin, tenantId = Tenant },
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
        await _writeAuthorizer.EnsureCanMutateAsync(ruleId, OwnerOf(deleted), userId, isAdmin, cancellationToken)
            .ConfigureAwait(false);

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
        await _writeAuthorizer.EnsureCanMutateAsync(ruleId, OwnerOf(deleted), userId, isAdmin, cancellationToken)
            .ConfigureAwait(false);

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
            "MATCH (r:Rule {id: $ruleId}) WHERE r.deletedAt IS NOT NULL " +
            "AND coalesce(r.tenantId, 'default') = $tenantId RETURN r",
            new { ruleId, tenantId = Tenant },
            ct).ConfigureAwait(false);
        return rows.Count == 0
            ? null
            : NodeMapper.ExtractProperties(rows[0].TryGetValue("r", out var r) ? r : null);
    }

    /// <summary>Extracts the owner id from a soft-deleted rule's property row (C2 authorizer input).</summary>
    /// <param name="props">The rule node's properties.</param>
    /// <returns>The owner id, or <see langword="null"/> for a global rule.</returns>
    private static string? OwnerOf(IReadOnlyDictionary<string, object?> props)
        => props.TryGetValue("ownerId", out var o) ? o?.ToString() : null;

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
