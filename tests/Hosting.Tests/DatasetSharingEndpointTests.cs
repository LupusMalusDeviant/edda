using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Gateway.Api;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Edda.Hosting.Tests;

/// <summary>
/// Unit tests for the dataset-sharing endpoint handlers (ADR-0014, Slice 2b-Transport): they map the
/// Owner-gated <see cref="IDatasetSharingService"/> onto clean HTTP results — 204 success, 401 without a
/// user, 400 for a bad role, 403 when the service rejects the caller.
/// </summary>
public sealed class DatasetSharingEndpointTests
{
    private sealed record FakeIdentity(string? UserId) : IIdentityContext
    {
        public string? Username => UserId;
        public string TenantId => "t1";
        public bool IsClone => false;
        public bool IsAdmin => false;
        public TenantRole Role => TenantRole.Viewer;
    }

    private sealed class RecordingSharing : IDatasetSharingService
    {
        public (string Dataset, string User, TenantRole Role)? Shared { get; private set; }
        public (string Dataset, string User)? Unshared { get; private set; }
        public bool Throw { get; init; }

        public Task ShareAsync(
            string datasetId, string targetUserId, TenantRole role, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new UnauthorizedAccessException();
            Shared = (datasetId, targetUserId, role);
            return Task.CompletedTask;
        }

        public Task UnshareAsync(string datasetId, string targetUserId, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new UnauthorizedAccessException();
            Unshared = (datasetId, targetUserId);
            return Task.CompletedTask;
        }
    }

    private static DatasetShareRequest Request(string userId, string role)
        => new() { UserId = userId, Role = role };

    [Fact]
    public async Task ShareDataset_ValidRequest_ReturnsNoContentAndShares()
    {
        var sharing = new RecordingSharing();

        var result = await AkgEndpointHandlers.ShareDatasetAsync(
            "git:repo", Request("alice", "editor"), new FakeIdentity("local"), sharing, CancellationToken.None);

        result.Should().BeOfType<NoContent>();
        sharing.Shared.Should().Be(("git:repo", "alice", TenantRole.Editor));
    }

    [Fact]
    public async Task ShareDataset_UnknownRole_ReturnsBadRequest()
    {
        var result = await AkgEndpointHandlers.ShareDatasetAsync(
            "git:repo", Request("alice", "superuser"), new FakeIdentity("local"), new RecordingSharing(),
            CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ShareDataset_EmptyUserId_ReturnsBadRequest()
    {
        var result = await AkgEndpointHandlers.ShareDatasetAsync(
            "git:repo", Request("  ", "editor"), new FakeIdentity("local"), new RecordingSharing(),
            CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ShareDataset_NoIdentityUser_ReturnsUnauthorized()
    {
        var result = await AkgEndpointHandlers.ShareDatasetAsync(
            "git:repo", Request("alice", "editor"), new FakeIdentity(null), new RecordingSharing(),
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task ShareDataset_ServiceRejects_ReturnsForbidden()
    {
        var result = await AkgEndpointHandlers.ShareDatasetAsync(
            "git:repo", Request("alice", "editor"), new FakeIdentity("local"), new RecordingSharing { Throw = true },
            CancellationToken.None);

        result.Should().BeOfType<ForbidHttpResult>();
    }

    [Fact]
    public async Task UnshareDataset_ValidRequest_ReturnsNoContentAndRevokes()
    {
        var sharing = new RecordingSharing();

        var result = await AkgEndpointHandlers.UnshareDatasetAsync(
            "git:repo", "alice", new FakeIdentity("local"), sharing, CancellationToken.None);

        result.Should().BeOfType<NoContent>();
        sharing.Unshared.Should().Be(("git:repo", "alice"));
    }

    [Fact]
    public async Task UnshareDataset_NoIdentityUser_ReturnsUnauthorized()
    {
        var result = await AkgEndpointHandlers.UnshareDatasetAsync(
            "git:repo", "alice", new FakeIdentity(null), new RecordingSharing(), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    [Fact]
    public async Task UnshareDataset_ServiceRejects_ReturnsForbidden()
    {
        var result = await AkgEndpointHandlers.UnshareDatasetAsync(
            "git:repo", "alice", new FakeIdentity("local"), new RecordingSharing { Throw = true },
            CancellationToken.None);

        result.Should().BeOfType<ForbidHttpResult>();
    }
}
