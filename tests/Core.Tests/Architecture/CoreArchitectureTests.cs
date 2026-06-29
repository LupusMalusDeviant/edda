using NetArchTest.Rules;

namespace Edda.Core.Tests.Architecture;

/// <summary>
/// Architecture tests enforcing the dependency rules defined in 02_solution-structure.md.
/// These tests must remain green at all times — violations are build failures.
/// </summary>
public class CoreArchitectureTests
{
    [Fact]
    public void Core_HasNoReferences_ToOtherSrcProjects()
    {
        // Core must never reference Agent, AKG, Security, Memory, Sandboxing, Gateway, or Channels.
        var forbiddenNamespaces = new[]
        {
            "Edda.Agent",
            "Edda.AKG",
            "Edda.Security",
            "Edda.Sandboxing",
            "Edda.Memory",
            "Edda.Gateway",
            "Edda.Channels"
        };

        var result = Types.InAssembly(typeof(Abstractions.IModelClient).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Core must not depend on any other src/ project. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_AllInterfaces_AreInAbstractionsNamespace()
    {
        // Every interface in Core (except IChannelContext which is a model contract)
        // must live in Edda.Core.Abstractions.
        var result = Types.InAssembly(typeof(Abstractions.IModelClient).Assembly)
            .That()
            .AreInterfaces()
            .And()
            .DoNotResideInNamespaceMatching(@"Edda\.Core\.Models.*")
            .Should()
            .ResideInNamespaceMatching(@"Edda\.Core\.Abstractions.*")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "All Core interfaces (except model-level interfaces in Core.Models) " +
                     "must reside in Edda.Core.Abstractions. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_HasNoServiceImplementations_InAbstractionsNamespace()
    {
        // No concrete classes (services) may live in Core.Abstractions — only interfaces.
        var result = Types.InAssembly(typeof(Abstractions.IModelClient).Assembly)
            .That()
            .ResideInNamespaceMatching(@"Edda\.Core\.Abstractions.*")
            .Should()
            .BeInterfaces()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Edda.Core.Abstractions must contain only interfaces. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_AllExceptions_InheritFromException()
    {
        var result = Types.InAssembly(typeof(Abstractions.IModelClient).Assembly)
            .That()
            .ResideInNamespaceMatching(@"Edda\.Core\.Exceptions.*")
            .Should()
            .Inherit(typeof(Exception))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "All types in Core.Exceptions must inherit from Exception. " +
                     "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
