using Edda.AKG.Authorization;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Tests.Authorization;

/// <summary>
/// Unit tests for <see cref="PassThroughDatasetWriteAuthorizer"/> (ADR-0014): every call delegates straight
/// to the wrapped sync <see cref="IRuleAuthorizer"/> (behaviour-neutral when dataset permissions are off).
/// </summary>
public sealed class PassThroughDatasetWriteAuthorizerTests
{
    private readonly Mock<IRuleAuthorizer> _inner = new();
    private readonly PassThroughDatasetWriteAuthorizer _sut;

    public PassThroughDatasetWriteAuthorizerTests() => _sut = new PassThroughDatasetWriteAuthorizer(_inner.Object);

    private static KnowledgeRule Rule(string id)
        => new() { Id = id, Type = "Rule", Domain = "general", Priority = RulePriority.Medium, Body = "b" };

    [Fact]
    public async Task RuleOverload_DelegatesToInner()
    {
        await _sut.EnsureCanMutateAsync(Rule("git:repo:x"), "alice", isAdmin: true);

        _inner.Verify(a => a.EnsureCanMutate(It.IsAny<KnowledgeRule>(), "alice", true), Times.Once);
    }

    [Fact]
    public async Task OwnerIdOverload_DelegatesToInner()
    {
        await _sut.EnsureCanMutateAsync("git:repo:x", ownerId: "bob", "alice");

        _inner.Verify(a => a.EnsureCanMutate("bob", "alice", false), Times.Once);
    }

    [Fact]
    public async Task DelegationPropagatesInnerException()
    {
        _inner.Setup(a => a.EnsureCanMutate(It.IsAny<KnowledgeRule>(), It.IsAny<string>(), It.IsAny<bool>()))
              .Throws<UnauthorizedAccessException>();

        var act = () => _sut.EnsureCanMutateAsync(Rule("git:repo:x"), "alice");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
