using Edda.AKG.Authorization;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Moq;

namespace Edda.AKG.Tests.Authorization;

/// <summary>
/// C2: the pure Owner/Editor/Viewer permission matrix of <see cref="RuleAuthorizer"/> —
/// including the legacy (null-identity) fallback and the admin overrides.
/// </summary>
public sealed class RuleAuthorizerTests
{
    private static IIdentityContext Identity(TenantRole role, bool isAdmin = false)
    {
        var identity = new Mock<IIdentityContext>();
        identity.SetupGet(i => i.UserId).Returns("u1");
        identity.SetupGet(i => i.TenantId).Returns(Tenants.DefaultTenantId);
        identity.SetupGet(i => i.IsAdmin).Returns(isAdmin);
        identity.SetupGet(i => i.Role).Returns(role);
        return identity.Object;
    }

    private static KnowledgeRule Rule(string? owner) => new()
    {
        Id = "r1",
        Type = "Rule",
        Domain = "testing",
        Priority = RulePriority.Medium,
        Body = "b",
        OwnerId = owner,
    };

    [Fact]
    public void CanMutateOwn_Viewer_False()
        => new RuleAuthorizer(Identity(TenantRole.Viewer)).CanMutateOwn().Should().BeFalse();

    [Fact]
    public void CanMutateOwn_Editor_True()
        => new RuleAuthorizer(Identity(TenantRole.Editor)).CanMutateOwn().Should().BeTrue();

    [Fact]
    public void CanMutateOwn_NullIdentity_True()
        => new RuleAuthorizer().CanMutateOwn().Should().BeTrue("legacy semantics permit user-scoped writes");

    [Fact]
    public void CanMutateOwn_AmbientAdminViewer_True()
        => new RuleAuthorizer(Identity(TenantRole.Viewer, isAdmin: true)).CanMutateOwn()
            .Should().BeTrue("the operator flag overrides the role matrix");

    [Fact]
    public void EnsureCanMutate_EditorOwnRule_Passes()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Editor)).EnsureCanMutate(Rule("u1"), "u1");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanMutate_EditorForeignRule_Throws()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Editor)).EnsureCanMutate(Rule("other"), "u1");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void EnsureCanMutate_ViewerOwnRule_Throws()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Viewer)).EnsureCanMutate(Rule("u1"), "u1");
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void EnsureCanMutate_OwnerForeignRule_Passes()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Owner)).EnsureCanMutate(Rule("other"), "u1");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanMutate_AdminParameter_OverridesViewer()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Viewer))
            .EnsureCanMutate(Rule("other"), "u1", isAdmin: true);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanMutate_AmbientAdmin_OverridesViewer()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Viewer, isAdmin: true))
            .EnsureCanMutate(Rule("other"), "u1");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanMutate_NullIdentity_LegacyOwnerCheck()
    {
        var authorizer = new RuleAuthorizer();

        var own = () => authorizer.EnsureCanMutate(Rule("u1"), "u1");
        var foreign = () => authorizer.EnsureCanMutate(Rule("other"), "u1");

        own.Should().NotThrow("the pre-C2 check permitted owners");
        foreign.Should().Throw<UnauthorizedAccessException>("the pre-C2 check rejected non-owners");
    }

    [Fact]
    public void EnsureCanMutate_OwnerIdOverload_GlobalRuleRequiresOwner()
    {
        var editor = () => new RuleAuthorizer(Identity(TenantRole.Editor)).EnsureCanMutate((string?)null, "u1");
        var owner = () => new RuleAuthorizer(Identity(TenantRole.Owner)).EnsureCanMutate((string?)null, "u1");

        editor.Should().Throw<UnauthorizedAccessException>("a global rule is not the editor's own");
        owner.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanAdminister_Owner_Passes()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Owner)).EnsureCanAdminister();
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanAdminister_Editor_Throws()
    {
        var act = () => new RuleAuthorizer(Identity(TenantRole.Editor)).EnsureCanAdminister();
        act.Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void EnsureCanAdminister_NullIdentity_RequiresAdminFlag()
    {
        var authorizer = new RuleAuthorizer();

        var withoutFlag = () => authorizer.EnsureCanAdminister();
        var withFlag = () => authorizer.EnsureCanAdminister(isAdmin: true);

        withoutFlag.Should().Throw<UnauthorizedAccessException>("legacy admin gates only honour the flag");
        withFlag.Should().NotThrow();
    }
}
