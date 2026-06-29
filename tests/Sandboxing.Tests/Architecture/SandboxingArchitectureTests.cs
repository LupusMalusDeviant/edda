using NetArchTest.Rules;

namespace Edda.Sandboxing.Tests.Architecture;

/// <summary>
/// Architecture tests enforcing the dependency rules for the Sandboxing project.
/// Per 02_solution-structure.md: Sandboxing → Core ONLY.
/// Sandboxing must NOT depend on Agent, AKG, Gateway, or Channels.
/// </summary>
public class SandboxingArchitectureTests
{
    private static readonly System.Reflection.Assembly SandboxingAssembly =
        typeof(DependencyInjection.SandboxingServiceExtensions).Assembly;

    [Fact]
    public void Sandboxing_DoesNotDependOn_Agent()
    {
        var result = Types.InAssembly(SandboxingAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Agent")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Sandboxing provides ISandboxFactory; it must not depend on Agent. " +
                     "Agent depends on Sandboxing for TDK isolation, not vice versa. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Sandboxing_DoesNotDependOn_Akg()
    {
        var result = Types.InAssembly(SandboxingAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.AKG")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Sandboxing must only depend on Core. AKG is a peer layer. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Sandboxing_DoesNotDependOn_Gateway()
    {
        var result = Types.InAssembly(SandboxingAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Gateway")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Sandboxing must not reference the Gateway composition root. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Sandboxing_AllTypes_AreInSandboxingNamespace()
    {
        var result = Types.InAssembly(SandboxingAssembly)
            .Should()
            .ResideInNamespaceMatching(@"Edda\.Sandboxing.*")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "All Sandboxing types must reside in the Edda.Sandboxing.* namespace. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
