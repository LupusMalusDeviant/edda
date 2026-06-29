using NetArchTest.Rules;

namespace Edda.Security.Tests.Architecture;

/// <summary>
/// Architecture tests enforcing the dependency rules for the Security project.
/// Per 02_solution-structure.md: Security → Core ONLY.
/// Security must NOT depend on Agent, AKG, Gateway, or Channels.
/// </summary>
public class SecurityArchitectureTests
{
    private static readonly System.Reflection.Assembly SecurityAssembly =
        typeof(DependencyInjection.SecurityServiceExtensions).Assembly;

    [Fact]
    public void Security_DoesNotDependOn_Agent()
    {
        var result = Types.InAssembly(SecurityAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Agent")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Security is a cross-cutting concern; it must not depend on Agent. " +
                     "Agent depends on Security, not vice versa. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Security_DoesNotDependOn_Akg()
    {
        var result = Types.InAssembly(SecurityAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.AKG")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Security must only depend on Core. AKG is a peer layer. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Security_DoesNotDependOn_Gateway()
    {
        var result = Types.InAssembly(SecurityAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Gateway")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Security must not reference the Gateway composition root. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Security_AllTypes_AreInSecurityNamespace()
    {
        // Compiler-generated types (anonymous types, closures) have names starting with '<'
        // and do not reside in any user-defined namespace — exclude them from this check.
        var result = Types.InAssembly(SecurityAssembly)
            .That()
            .DoNotHaveNameStartingWith("<")
            .And()
            .DoNotHaveName("Program")
            .Should()
            .ResideInNamespaceMatching(@"Edda\.Security.*")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "All Security types must reside in the Edda.Security.* namespace. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
