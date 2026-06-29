using NetArchTest.Rules;

namespace Edda.AKG.Mcp.Tests.Architecture;

/// <summary>
/// Architecture tests enforcing the dependency rules for the AKG.Mcp project.
/// Per 02_solution-structure.md: AKG.Mcp → Core + AKG.
/// AKG.Mcp must NOT depend on Agent, Gateway, Channels, Security, Memory, or Sandboxing.
/// </summary>
public class AkgMcpArchitectureTests
{
    private static readonly System.Reflection.Assembly AkgMcpAssembly =
        typeof(DependencyInjection.McpServiceExtensions).Assembly;

    [Fact]
    public void AkgMcp_DoesNotDependOn_Agent()
    {
        var result = Types.InAssembly(AkgMcpAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Agent")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AKG.Mcp bridges AKG to external MCP clients; it must not depend on Agent. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void AkgMcp_DoesNotDependOn_Gateway()
    {
        var result = Types.InAssembly(AkgMcpAssembly)
            .ShouldNot()
            .HaveDependencyOn("Edda.Gateway")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AKG.Mcp must not reference the Gateway composition root. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void AkgMcp_DoesNotDependOn_SecurityOrMemoryOrSandboxing()
    {
        var forbidden = new[] { "Edda.Security", "Edda.Memory", "Edda.Sandboxing" };

        var result = Types.InAssembly(AkgMcpAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "AKG.Mcp must only depend on Core and AKG. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void AkgMcp_AllTypes_AreInAkgMcpNamespace()
    {
        // Compiler-generated types (anonymous types, closures, BCL helpers) have names
        // starting with '<' and no user-defined namespace — exclude them from this check.
        var result = Types.InAssembly(AkgMcpAssembly)
            .That()
            .ResideInNamespaceMatching(@"Edda\..*")
            .And()
            .DoNotHaveNameStartingWith("<")
            .And()
            .DoNotHaveName("Program")
            .Should()
            .ResideInNamespaceMatching(@"Edda\.AKG\.Mcp.*")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "All AKG.Mcp types must reside in the Edda.AKG.Mcp.* namespace. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
