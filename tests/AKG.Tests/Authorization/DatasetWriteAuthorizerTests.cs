using Edda.AKG.Authorization;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Tests.Authorization;

/// <summary>
/// Unit tests for <see cref="DatasetWriteAuthorizer"/> (ADR-0014, Slice 2b): an Editor+ grant on the rule's
/// dataset permits mutation without delegating; anything else (Viewer, no grant, non-dataset rule) falls
/// through to the wrapped sync <see cref="IRuleAuthorizer"/>.
/// </summary>
public sealed class DatasetWriteAuthorizerTests
{
    private readonly Mock<IRuleAuthorizer> _inner = new();
    private readonly Mock<IDatasetGrantStore> _grants = new();
    private readonly DatasetWriteAuthorizer _sut;

    public DatasetWriteAuthorizerTests()
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.TenantId).Returns("t1");
        _sut = new DatasetWriteAuthorizer(_inner.Object, _grants.Object, identity.Object);
    }

    private static KnowledgeRule Rule(string id)
        => new() { Id = id, Type = "Rule", Domain = "general", Priority = RulePriority.Medium, Body = "b" };

    private void SetupGrant(string dataset, TenantRole? role)
        => _grants.Setup(g => g.GetRoleAsync("t1", dataset, "alice", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(role);

    [Theory]
    [InlineData(TenantRole.Editor)]
    [InlineData(TenantRole.Owner)]
    public async Task RuleOverload_EditorOrOwnerGrant_AllowsWithoutDelegating(TenantRole role)
    {
        SetupGrant("git:repo", role);

        await _sut.EnsureCanMutateAsync(Rule("git:repo:x"), "alice");

        _inner.Verify(a => a.EnsureCanMutate(
            It.IsAny<KnowledgeRule>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RuleOverload_ViewerGrant_Delegates()
    {
        SetupGrant("git:repo", TenantRole.Viewer);

        await _sut.EnsureCanMutateAsync(Rule("git:repo:x"), "alice");

        _inner.Verify(a => a.EnsureCanMutate(It.IsAny<KnowledgeRule>(), "alice", false), Times.Once);
    }

    [Fact]
    public async Task RuleOverload_NoGrant_Delegates()
    {
        SetupGrant("git:repo", null);

        await _sut.EnsureCanMutateAsync(Rule("git:repo:x"), "alice");

        _inner.Verify(a => a.EnsureCanMutate(It.IsAny<KnowledgeRule>(), "alice", false), Times.Once);
    }

    [Fact]
    public async Task RuleOverload_NonDatasetRule_DelegatesWithoutQueryingGrants()
    {
        await _sut.EnsureCanMutateAsync(Rule("hand-authored"), "alice");

        _inner.Verify(a => a.EnsureCanMutate(It.IsAny<KnowledgeRule>(), "alice", false), Times.Once);
        _grants.Verify(g => g.GetRoleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RuleOverload_DelegationPropagatesUnauthorized()
    {
        SetupGrant("git:repo", null);
        _inner.Setup(a => a.EnsureCanMutate(It.IsAny<KnowledgeRule>(), It.IsAny<string>(), It.IsAny<bool>()))
              .Throws<UnauthorizedAccessException>();

        var act = () => _sut.EnsureCanMutateAsync(Rule("git:repo:x"), "alice");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task OwnerIdOverload_EditorGrant_AllowsWithoutDelegating()
    {
        SetupGrant("git:repo", TenantRole.Editor);

        await _sut.EnsureCanMutateAsync("git:repo:x", ownerId: null, "alice");

        _inner.Verify(a => a.EnsureCanMutate(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task OwnerIdOverload_NoGrant_Delegates()
    {
        SetupGrant("git:repo", null);

        await _sut.EnsureCanMutateAsync("git:repo:x", ownerId: "someone", "alice");

        _inner.Verify(a => a.EnsureCanMutate("someone", "alice", false), Times.Once);
    }
}
