using Edda.AKG.Datasets;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Tests.Datasets;

/// <summary>
/// Unit tests for <see cref="GrantBackedDatasetPermissionService"/> (ADR-0014): admins and identity-less
/// contexts are unrestricted; an authenticated user is restricted to the datasets the grant store returns.
/// </summary>
public sealed class GrantBackedDatasetPermissionServiceTests
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
    public async Task NoIdentity_Unrestricted()
    {
        var sut = new GrantBackedDatasetPermissionService(_grants.Object, identity: null);

        (await sut.ResolveVisibilityAsync()).IsUnrestricted.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_Unrestricted()
    {
        var sut = new GrantBackedDatasetPermissionService(_grants.Object, Identity("root", admin: true));

        (await sut.ResolveVisibilityAsync()).IsUnrestricted.Should().BeTrue();
    }

    [Fact]
    public async Task NoUserId_Unrestricted()
    {
        var sut = new GrantBackedDatasetPermissionService(_grants.Object, Identity(userId: null));

        (await sut.ResolveVisibilityAsync()).IsUnrestricted.Should().BeTrue();
    }

    [Fact]
    public async Task User_RestrictedToGrantedDatasets()
    {
        _grants.Setup(g => g.GetVisibleDatasetIdsAsync("t1", "alice", It.IsAny<CancellationToken>()))
               .ReturnsAsync(["git:a", "upload:b"]);
        var sut = new GrantBackedDatasetPermissionService(_grants.Object, Identity("alice"));

        var visibility = await sut.ResolveVisibilityAsync();

        visibility.IsUnrestricted.Should().BeFalse();
        visibility.VisibleDatasetIds.Should().BeEquivalentTo("git:a", "upload:b");
    }
}
