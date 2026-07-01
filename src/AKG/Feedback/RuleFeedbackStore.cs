using Edda.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Edda.AKG.Feedback;

/// <summary>
/// SQLite-backed persistence for rule feedback events and aggregated statistics.
/// Manages two tables: RuleFeedbackEvents (raw events) and RuleFeedbackStats (aggregated data).
/// Thread-safe: all operations acquire the connection per call; SQLite's WAL mode is used.
/// </summary>
internal sealed class RuleFeedbackStore : IRuleFeedbackStore
{
    private readonly string _connectionString;
    private readonly ILogger<RuleFeedbackStore> _logger;

    /// <summary>
    /// Initializes a new <see cref="RuleFeedbackStore"/> and ensures the schema exists.
    /// </summary>
    /// <param name="dbPath">Path to the SQLite database file (e.g. data/feedback.db).</param>
    /// <param name="logger">Structured logger.</param>
    public RuleFeedbackStore(string dbPath, ILogger<RuleFeedbackStore> logger)
    {
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;";
        _logger = logger;
        EnsureSchema();
    }

    // ── Schema ─────────────────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS RuleFeedbackEvents (
                EventId        TEXT PRIMARY KEY,
                RuleId         TEXT NOT NULL,
                EventType      TEXT NOT NULL,
                Positive       INTEGER NOT NULL,
                UserId         TEXT,
                ConversationId TEXT,
                Timestamp      TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_feedback_rule ON RuleFeedbackEvents(RuleId);
            CREATE INDEX IF NOT EXISTS idx_feedback_conv ON RuleFeedbackEvents(ConversationId);

            CREATE TABLE IF NOT EXISTS RuleFeedbackStats (
                RuleId               TEXT PRIMARY KEY,
                TdkPassCount         INTEGER NOT NULL DEFAULT 0,
                TdkFailCount         INTEGER NOT NULL DEFAULT 0,
                UserPositiveCount    INTEGER NOT NULL DEFAULT 0,
                UserNegativeCount    INTEGER NOT NULL DEFAULT 0,
                ComplianceCount      INTEGER NOT NULL DEFAULT 0,
                NonComplianceCount   INTEGER NOT NULL DEFAULT 0,
                UsageCount           INTEGER NOT NULL DEFAULT 0,
                ConfidenceMultiplier REAL    NOT NULL DEFAULT 1.0,
                LastRecalculated     TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // ── Write ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a feedback event and increments the matching counter in RuleFeedbackStats.
    /// </summary>
    public async Task AppendEventAsync(RuleFeedbackEvent evt, CancellationToken ct)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            // Insert raw event
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT OR IGNORE INTO RuleFeedbackEvents
                    (EventId, RuleId, EventType, Positive, UserId, ConversationId, Timestamp)
                VALUES ($id, $ruleId, $type, $positive, $userId, $convId, $ts)
                """;
            insertCmd.Parameters.AddWithValue("$id",       evt.EventId);
            insertCmd.Parameters.AddWithValue("$ruleId",   evt.RuleId);
            insertCmd.Parameters.AddWithValue("$type",     evt.Type.ToString());
            insertCmd.Parameters.AddWithValue("$positive", evt.Positive ? 1 : 0);
            insertCmd.Parameters.AddWithValue("$userId",   (object?)evt.UserId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$convId",   (object?)evt.ConversationId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$ts",       evt.Timestamp.ToString("O"));
            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            // Upsert stats counter
            var (col, increment) = GetCounterColumn(evt);
            using var statsCmd = conn.CreateCommand();
            statsCmd.Transaction = tx;
            statsCmd.CommandText = $"""
                INSERT INTO RuleFeedbackStats (RuleId, {col})
                VALUES ($ruleId, $inc)
                ON CONFLICT(RuleId) DO UPDATE SET {col} = {col} + $inc
                """;
            statsCmd.Parameters.AddWithValue("$ruleId", evt.RuleId);
            statsCmd.Parameters.AddWithValue("$inc",    increment);
            await statsCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist feedback event {EventId} for rule {RuleId} | {Component}",
                evt.EventId, evt.RuleId, "AKG.Feedback");
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Increments the UsageCount for a rule without recording a raw event.
    /// </summary>
    public async Task IncrementUsageAsync(string ruleId, CancellationToken ct)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RuleFeedbackStats (RuleId, UsageCount)
            VALUES ($ruleId, 1)
            ON CONFLICT(RuleId) DO UPDATE SET UsageCount = UsageCount + 1
            """;
        cmd.Parameters.AddWithValue("$ruleId", ruleId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the ConfidenceMultiplier and LastRecalculated for a rule.
    /// </summary>
    public async Task UpdateMultiplierAsync(
        string ruleId, double multiplier, DateTimeOffset recalculatedAt, CancellationToken ct)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RuleFeedbackStats (RuleId, ConfidenceMultiplier, LastRecalculated)
            VALUES ($ruleId, $multiplier, $at)
            ON CONFLICT(RuleId) DO UPDATE
                SET ConfidenceMultiplier = $multiplier,
                    LastRecalculated     = $at
            """;
        cmd.Parameters.AddWithValue("$ruleId",     ruleId);
        cmd.Parameters.AddWithValue("$multiplier", multiplier);
        cmd.Parameters.AddWithValue("$at",         recalculatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    // ── Read ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all rules that have at least one feedback event recorded.
    /// </summary>
    public async Task<IReadOnlyList<RuleFeedbackStats>> GetAllStatsAsync(CancellationToken ct)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.*,
                   (SELECT MAX(e.Timestamp) FROM RuleFeedbackEvents e WHERE e.RuleId = s.RuleId) AS LastFeedbackAt
            FROM RuleFeedbackStats s
            """;
        var result = new List<RuleFeedbackStats>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(MapStats(reader));
        return result;
    }

    /// <summary>
    /// Aggregates a single user's feedback events into per-rule statistics (counts only;
    /// <see cref="RuleFeedbackStats.ConfidenceMultiplier"/> is left neutral). Used to compute a
    /// user-specific confidence overlay at read time without a per-user stats schema.
    /// </summary>
    /// <param name="userId">The user whose events to aggregate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Per-rule feedback statistics derived from that user's events.</returns>
    public async Task<IReadOnlyList<RuleFeedbackStats>> GetUserStatsAsync(string userId, CancellationToken ct)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT RuleId,
                SUM(CASE WHEN EventType = 'TdkValidation'   AND Positive = 1 THEN 1 ELSE 0 END) AS TdkPass,
                SUM(CASE WHEN EventType = 'TdkValidation'   AND Positive = 0 THEN 1 ELSE 0 END) AS TdkFail,
                SUM(CASE WHEN EventType = 'UserFeedback'    AND Positive = 1 THEN 1 ELSE 0 END) AS UserPos,
                SUM(CASE WHEN EventType = 'UserFeedback'    AND Positive = 0 THEN 1 ELSE 0 END) AS UserNeg,
                SUM(CASE WHEN EventType = 'ComplianceCheck' AND Positive = 1 THEN 1 ELSE 0 END) AS Comp,
                SUM(CASE WHEN EventType = 'ComplianceCheck' AND Positive = 0 THEN 1 ELSE 0 END) AS NonComp
            FROM RuleFeedbackEvents
            WHERE UserId = $userId
            GROUP BY RuleId
            """;
        cmd.Parameters.AddWithValue("$userId", userId);

        var result = new List<RuleFeedbackStats>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add(new RuleFeedbackStats
            {
                RuleId             = reader.GetString(0),
                TdkPassCount       = Convert.ToInt32(reader.GetValue(1)),
                TdkFailCount       = Convert.ToInt32(reader.GetValue(2)),
                UserPositiveCount  = Convert.ToInt32(reader.GetValue(3)),
                UserNegativeCount  = Convert.ToInt32(reader.GetValue(4)),
                ComplianceCount    = Convert.ToInt32(reader.GetValue(5)),
                NonComplianceCount = Convert.ToInt32(reader.GetValue(6)),
            });
        }

        return result;
    }

    /// <summary>
    /// Returns feedback statistics for a specific rule.
    /// Returns a default neutral record if no data exists yet.
    /// </summary>
    public async Task<RuleFeedbackStats> GetStatsForRuleAsync(string ruleId, CancellationToken ct)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.*,
                   (SELECT MAX(e.Timestamp) FROM RuleFeedbackEvents e WHERE e.RuleId = s.RuleId) AS LastFeedbackAt
            FROM RuleFeedbackStats s
            WHERE s.RuleId = $ruleId
            """;
        cmd.Parameters.AddWithValue("$ruleId", ruleId);
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? MapStats(reader)
            : new RuleFeedbackStats { RuleId = ruleId };
    }

    /// <summary>
    /// Returns the conversation IDs for the rules active in a given conversation.
    /// Used to propagate user feedback to all rules that contributed to the response.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetActiveRulesForConversationAsync(
        string conversationId, CancellationToken ct)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT RuleId FROM RuleFeedbackEvents
            WHERE ConversationId = $convId
            """;
        cmd.Parameters.AddWithValue("$convId", conversationId);
        var result = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            result.Add(reader.GetString(0));
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static (string Column, int Increment) GetCounterColumn(RuleFeedbackEvent evt) =>
        (evt.Type, evt.Positive) switch
        {
            (FeedbackEventType.TdkValidation, true)   => ("TdkPassCount", 1),
            (FeedbackEventType.TdkValidation, false)  => ("TdkFailCount", 1),
            (FeedbackEventType.UserFeedback,  true)   => ("UserPositiveCount", 1),
            (FeedbackEventType.UserFeedback,  false)  => ("UserNegativeCount", 1),
            (FeedbackEventType.ComplianceCheck, true) => ("ComplianceCount", 1),
            (FeedbackEventType.ComplianceCheck, false)=> ("NonComplianceCount", 1),
            _                                         => ("UsageCount", 0),
        };

    private static RuleFeedbackStats MapStats(SqliteDataReader r) =>
        new()
        {
            RuleId               = r.GetString(r.GetOrdinal("RuleId")),
            TdkPassCount         = r.GetInt32(r.GetOrdinal("TdkPassCount")),
            TdkFailCount         = r.GetInt32(r.GetOrdinal("TdkFailCount")),
            UserPositiveCount    = r.GetInt32(r.GetOrdinal("UserPositiveCount")),
            UserNegativeCount    = r.GetInt32(r.GetOrdinal("UserNegativeCount")),
            ComplianceCount      = r.GetInt32(r.GetOrdinal("ComplianceCount")),
            NonComplianceCount   = r.GetInt32(r.GetOrdinal("NonComplianceCount")),
            UsageCount           = r.GetInt32(r.GetOrdinal("UsageCount")),
            ConfidenceMultiplier = r.GetDouble(r.GetOrdinal("ConfidenceMultiplier")),
            LastRecalculated     = r.IsDBNull(r.GetOrdinal("LastRecalculated"))
                ? null
                : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LastRecalculated"))),
            LastFeedbackAt       = r.IsDBNull(r.GetOrdinal("LastFeedbackAt"))
                ? null
                : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("LastFeedbackAt"))),
        };
}
