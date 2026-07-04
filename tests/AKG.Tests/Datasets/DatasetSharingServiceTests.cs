using Edda.AKG.Datasets;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Tests.Datasets;

/// <summary>
/// Unit tests for <see cref="DatasetSharingService"/> (ADR-0014): only a tenant admin or the dataset Owner may
/// grant/revoke; everyone else is rejected without touching the store.
/// </summary>
public sealed class DatasetSharingServiceTests
{
    private readonly Mock<IDatasetGrantStore> _grants = new();

    private static IIdentityContext Identity(string? userId, string tenant = "t1", bool admin = false)
    {
        var id = new Mock<IIdentityContext>();
        id.SetupGet(i => i.UserId).Returns(userId);
        id.SetupGet(i => i.TenantId).Returns(tenant);
        id.SetupGet(i => i.IsAdmin).Returns(admin);
        return id.Object;
    }

    [Fact]
    public async Task ShareAsync_Admin_GrantsRole()
    {
        var sut = new DatasetSharingService(_grants.Object, Identity("root", admin: true));

        await sut.ShareAsync("git:repo", "alice", TenantRole.Editor);

        _grants.Verify(g => g.GrantAsync(
            It.Is<DatasetGrant>(x => x.TenantId == "t1" && x.DatasetId == "git:repo"
                && x.UserId == "alice" && x.Role == TenantRole.Editor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShareAsync_DatasetOwner_GrantsRole()
    {
        _grants.Setup(g => g.GetRoleAsync("t1", "git:repo", "owner", It.IsAny<CancellationToken>()))
               .ReturnsAsync(TenantRole.Owner);
        var sut = new DatasetSharingService(_grants.Object, Identity("owner"));

        await sut.ShareAsync("git:repo", "alice", TenantRole.Viewer);

        _grants.Verify(g => g.GrantAsync(It.IsAny<DatasetGrant>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShareAsync_NonOwner_ThrowsAndDoesNotGrant()
    {
        _grants.Setup(g => g.GetRoleAsync("t1", "git:repo", "editor", It.IsAny<CancellationToken>()))
               .ReturnsAsync(TenantRole.Editor);
        var sut = new DatasetSharingService(_grants.Object, Identity("editor"));

        var act = () => sut.ShareAsync("git:repo", "alice", TenantRole.Viewer);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _grants.Verify(g => g.GrantAsync(It.IsAny<DatasetGrant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnshareAsync_DatasetOwner_Revokes()
    {
        _grants.Setup(g => g.GetRoleAsync("t1", "git:repo", "owner", It.IsAny<CancellationToken>()))
               .ReturnsAsync(TenantRole.Owner);
        var sut = new DatasetSharingService(_grants.Object, Identity("owner"));

        await sut.UnshareAsync("git:repo", "alice");

        _grants.Verify(g => g.RevokeAsync("t1", "git:repo", "alice", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnshareAsync_Unauthorized_ThrowsAndDoesNotRevoke()
    {
        _grants.Setup(g => g.GetRoleAsync(
                   It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((TenantRole?)null);
        var sut = new DatasetSharingService(_grants.Object, Identity("stranger"));

        var act = () => sut.UnshareAsync("git:repo", "alice");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _grants.Verify(g => g.RevokeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
