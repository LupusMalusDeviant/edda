using Edda.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Graph;

/// <summary>
/// Validates the AKG graph structure by checking for cyclic IMPLIES dependencies
/// and dangling rule references.
/// </summary>
internal sealed class GraphValidator : IGraphValidator
{
    private readonly ICypherExecutor _cypher;
    private readonly ILogger<GraphValidator> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GraphValidator"/>.
    /// </summary>
    /// <param name="cypher">Cypher executor for validation queries.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public GraphValidator(ICypherExecutor cypher, ILogger<GraphValidator> logger)
    {
        _cypher = cypher;
        _logger = logger;
    }

    /// <summary>
    /// Validates the graph by checking for cycles and dangling references.
    /// Logs warnings for any issues found but does not throw.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the graph is structurally valid;
    /// <see langword="false"/> if any cycles or dangling references are detected.
    /// </returns>
    public async Task<bool> ValidateAsync(CancellationToken ct)
    {
        var hasCycles = await DetectCyclesAsync(ct).ConfigureAwait(false);
        var danglingCount = await DetectDanglingReferencesAsync(ct).ConfigureAwait(false);

        if (hasCycles)
            _logger.LogWarning("AKG validation: cyclic IMPLIES dependencies detected | {Component}", "AKG");

        if (danglingCount > 0)
            _logger.LogWarning("AKG validation: {Count} dangling rule references detected | {Component}", danglingCount, "AKG");

        return !hasCycles && danglingCount == 0;
    }

    private async Task<bool> DetectCyclesAsync(CancellationToken ct)
    {
        // Detect cycles in IMPLIES edges (length >= 2 cycles back to origin)
        var rows = await _cypher.QueryAsync(
            "MATCH p=(r:Rule)-[:IMPLIES*2..]->(r) RETURN count(p) AS cycles LIMIT 1",
            ct: ct).ConfigureAwait(false);

        if (rows.Count == 0) return false;
        var val = rows[0].TryGetValue("cycles", out var v) ? v : null;
        return Convert.ToInt64(val ?? 0L) > 0;
    }

    private async Task<long> DetectDanglingReferencesAsync(CancellationToken ct)
    {
        // Load all existing rule IDs
        var idRows = await _cypher.QueryAsync(
            "MATCH (r:Rule) RETURN r.id AS id",
            ct: ct).ConfigureAwait(false);

        var existingIds = idRows
            .Where(r => r.ContainsKey("id") && r["id"] != null)
            .Select(r => r["id"]!.ToString()!)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        // Load all rules with implies lists
        var ruleRows = await _cypher.QueryAsync(
            "MATCH (r:Rule) WHERE r.implies IS NOT NULL RETURN r.id AS id, r.implies AS implies",
            ct: ct).ConfigureAwait(false);

        long danglingCount = 0;
        foreach (var row in ruleRows)
        {
            if (!row.TryGetValue("implies", out var impliesVal))
                continue;

            IEnumerable<string> implies = impliesVal is IEnumerable<object> list
                ? list.Select(x => x?.ToString() ?? string.Empty).Where(s => s.Length > 0)
                : [];

            foreach (var implied in implies)
            {
                if (!existingIds.Contains(implied))
                    danglingCount++;
            }
        }

        return danglingCount;
    }
}
