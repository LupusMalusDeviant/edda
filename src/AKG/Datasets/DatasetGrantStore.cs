using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Data.Sqlite;

namespace Edda.AKG.Datasets;

/// <summary>
/// SQLite-backed <see cref="IDatasetGrantStore"/> (ADR-0014, Slice 2): one table of (tenant, dataset, user,
/// role) grants, keyed by the tenant/dataset/user triple. A local side-store analogous to the feedback store;
/// it holds no infrastructure dependency and is exercised in tests over a temp-file database.
/// </summary>
internal sealed class DatasetGrantStore : IDatasetGrantStore
{
    private const string GrantsTable = "DatasetGrants";
    private readonly string _connectionString;

    /// <summary>Initializes a new <see cref="DatasetGrantStore"/> and ensures the schema exists.</summary>
    /// <param name="dbPath">Path to the SQLite database file (e.g. data/datasets.db).</param>
    public DatasetGrantStore(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS {GrantsTable} (
                TenantId  TEXT NOT NULL,
                DatasetId TEXT NOT NULL,
                UserId    TEXT NOT NULL,
                Role      TEXT NOT NULL,
                PRIMARY KEY (TenantId, DatasetId, UserId)
            );

            CREATE INDEX IF NOT EXISTS idx_grants_user ON {GrantsTable}(TenantId, UserId);
            """;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task GrantAsync(DatasetGrant grant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {GrantsTable} (TenantId, DatasetId, UserId, Role)
            VALUES ($tenant, $dataset, $user, $role)
            ON CONFLICT(TenantId, DatasetId, UserId) DO UPDATE SET Role = excluded.Role
            """;
        cmd.Parameters.AddWithValue("$tenant", grant.TenantId);
        cmd.Parameters.AddWithValue("$dataset", grant.DatasetId);
        cmd.Parameters.AddWithValue("$user", grant.UserId);
        cmd.Parameters.AddWithValue("$role", grant.Role.ToString());
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        string tenantId, string datasetId, string userId, CancellationToken cancellationToken = default)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"DELETE FROM {GrantsTable} WHERE TenantId = $tenant AND DatasetId = $dataset AND UserId = $user";
        cmd.Parameters.AddWithValue("$tenant", tenantId);
        cmd.Parameters.AddWithValue("$dataset", datasetId);
        cmd.Parameters.AddWithValue("$user", userId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetVisibleDatasetIdsAsync(
        string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT DISTINCT DatasetId FROM {GrantsTable} WHERE TenantId = $tenant AND UserId = $user";
        cmd.Parameters.AddWithValue("$tenant", tenantId);
        cmd.Parameters.AddWithValue("$user", userId);

        var result = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(reader.GetString(0));
        return result;
    }

    /// <inheritdoc />
    public async Task<TenantRole?> GetRoleAsync(
        string tenantId, string datasetId, string userId, CancellationToken cancellationToken = default)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT Role FROM {GrantsTable} WHERE TenantId = $tenant AND DatasetId = $dataset AND UserId = $user";
        cmd.Parameters.AddWithValue("$tenant", tenantId);
        cmd.Parameters.AddWithValue("$dataset", datasetId);
        cmd.Parameters.AddWithValue("$user", userId);

        var raw = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return raw is string role && Enum.TryParse<TenantRole>(role, out var parsed) ? parsed : null;
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
