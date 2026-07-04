using Edda.AKG.Datasets;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Datasets;

/// <summary>
/// Unit tests for <see cref="DatasetGrantStore"/> (ADR-0014) over a temp-file SQLite database — grant upsert,
/// role lookup, tenant/user-scoped visibility and revoke.
/// </summary>
public sealed class DatasetGrantStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatasetGrantStore _store;

    public DatasetGrantStoreTests()
    {
        _dbPath = Path.GetTempFileName() + ".db";
        _store = new DatasetGrantStore(_dbPath);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best-effort cleanup */ }
    }

    private static DatasetGrant Grant(string tenant, string dataset, string user, TenantRole role)
        => new() { TenantId = tenant, DatasetId = dataset, UserId = user, Role = role };

    [Fact]
    public async Task Grant_ThenGetRole_ReturnsGrantedRole()
    {
        await _store.GrantAsync(Grant("t1", "git:repo", "alice", TenantRole.Owner));

        (await _store.GetRoleAsync("t1", "git:repo", "alice")).Should().Be(TenantRole.Owner);
    }

    [Fact]
    public async Task GetRole_NoGrant_ReturnsNull()
        => (await _store.GetRoleAsync("t1", "git:repo", "nobody")).Should().BeNull();

    [Fact]
    public async Task Grant_Twice_UpdatesRole()
    {
        await _store.GrantAsync(Grant("t1", "git:repo", "alice", TenantRole.Viewer));
        await _store.GrantAsync(Grant("t1", "git:repo", "alice", TenantRole.Editor));

        (await _store.GetRoleAsync("t1", "git:repo", "alice")).Should().Be(TenantRole.Editor);
    }

    [Fact]
    public async Task GetVisibleDatasetIds_ReturnsOnlyThisUsersDatasetsInThisTenant()
    {
        await _store.GrantAsync(Grant("t1", "git:a", "alice", TenantRole.Viewer));
        await _store.GrantAsync(Grant("t1", "upload:b", "alice", TenantRole.Editor));
        await _store.GrantAsync(Grant("t1", "git:c", "bob", TenantRole.Viewer));    // other user
        await _store.GrantAsync(Grant("t2", "git:d", "alice", TenantRole.Viewer));  // other tenant

        (await _store.GetVisibleDatasetIdsAsync("t1", "alice")).Should().BeEquivalentTo("git:a", "upload:b");
    }

    [Fact]
    public async Task Revoke_RemovesGrant()
    {
        await _store.GrantAsync(Grant("t1", "git:repo", "alice", TenantRole.Owner));

        await _store.RevokeAsync("t1", "git:repo", "alice");

        (await _store.GetRoleAsync("t1", "git:repo", "alice")).Should().BeNull();
        (await _store.GetVisibleDatasetIdsAsync("t1", "alice")).Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_MissingGrant_IsNoOp()
    {
        await _store.RevokeAsync("t1", "git:repo", "ghost");

        (await _store.GetRoleAsync("t1", "git:repo", "ghost")).Should().BeNull();
    }
}
