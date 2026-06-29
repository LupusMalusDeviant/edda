using NetArchTest.Rules;

namespace Edda.AKG.Tests.Architecture;

/// <summary>
/// Architecture tests enforcing the dependency rules for the AKG project.
/// Per 02_solution-structure.md: AKG → Core ONLY.
/// AKG must NOT depend on Agent, Gateway, Channels, Security, Memory, or Sandboxing.
/// </summary>
public class AkgArchitectureTests
{
    private static readonly System.Reflection.Assembly AkgAssembly =
        typeof(DependencyInjection.AkgServiceExtensions).Assembly;

    [Fact]
    public void Akg_DoesNotDependOn_Agent()
    {
        var result = Types.InAssembly(AkgAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Agent")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AKG must only depend on Core. Agent depends on AKG, not vice versa. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Akg_DoesNotDependOn_Gateway()
    {
        var result = Types.InAssembly(AkgAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Gateway")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AKG must not reference the Gateway composition root. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Akg_DoesNotDependOn_SecurityOrMemoryOrSandboxing()
    {
        var forbidden = new[] { "Edda.Security", "Edda.Memory", "Edda.Sandboxing" };

        var result = Types.InAssembly(AkgAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AKG must only depend on Core. Security/Memory/Sandboxing are peer layers. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Akg_DoesNotDependOn_Channels()
    {
        var result = Types.InAssembly(AkgAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Channels")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AKG must not depend on any channel implementation. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Akg_AllTypes_AreInAkgNamespace()
    {
        // Compiler-generated types (anonymous types, closures) have names starting with '<'
        // and do not reside in any user-defined namespace — exclude them from this check.
        // BCL-injected helper types (e.g. <>z__ReadOnlySingleElementList<T> and its nested
        // Enumerator) live in the global namespace; the ResideInNamespaceMatching pre-filter
        // excludes them by requiring a proper Edda.* namespace first.
        var result = Types.InAssembly(AkgAssembly)
            .That()
            .ResideInNamespaceMatching(@"Edda\..*")
            .And()
            .DoNotHaveNameStartingWith("<")
            .And()
            .DoNotHaveName("Program")
            .Should()
            .ResideInNamespaceMatching(@"Edda\.AKG.*")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "All AKG types must reside in the Edda.AKG.* namespace. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
