using Edda.AKG.Rules;
using Edda.Core.Models;

namespace Edda.AKG.Tests.Rules;

/// <summary>Unit tests for <see cref="BatchRuleOperations"/> (E8 pure rule mutation).</summary>
public sealed class BatchRuleOperationsTests
{
    private static KnowledgeRule Rule(IReadOnlyList<string>? tags = null, RulePriority priority = RulePriority.Medium)
        => new() { Id = "r1", Type = "Rule", Domain = "d", Priority = priority, Body = "b", Tags = tags ?? [] };

    [Fact]
    public void Apply_AddTag_NewTag_AppendsTag()
    {
        var op = new BatchRuleOperation { Type = BatchRuleOperationType.AddTag, Tag = "x" };

        var result = BatchRuleOperations.Apply(Rule(["a"]), op);

        result.Should().NotBeNull();
        result!.Tags.Should().BeEquivalentTo("a", "x");
    }

    [Fact]
    public void Apply_AddTag_Duplicate_ReturnsNull()
    {
        var op = new BatchRuleOperation { Type = BatchRuleOperationType.AddTag, Tag = "A" };   // case-insensitive

        BatchRuleOperations.Apply(Rule(["a"]), op).Should().BeNull();
    }

    [Fact]
    public void Apply_AddTag_BlankTag_ReturnsNull()
    {
        var op = new BatchRuleOperation { Type = BatchRuleOperationType.AddTag, Tag = "   " };

        BatchRuleOperations.Apply(Rule(["a"]), op).Should().BeNull();
    }

    [Fact]
    public void Apply_RemoveTag_Present_RemovesTag()
    {
        var op = new BatchRuleOperation { Type = BatchRuleOperationType.RemoveTag, Tag = "a" };

        var result = BatchRuleOperations.Apply(Rule(["a", "b"]), op);

        result.Should().NotBeNull();
        result!.Tags.Should().BeEquivalentTo("b");
    }

    [Fact]
    public void Apply_RemoveTag_Absent_ReturnsNull()
    {
        var op = new BatchRuleOperation { Type = BatchRuleOperationType.RemoveTag, Tag = "a" };

        BatchRuleOperations.Apply(Rule(["b"]), op).Should().BeNull();
    }

    [Fact]
    public void Apply_SetPriority_Different_SetsPriority()
    {
        var op = new BatchRuleOperation { Type = BatchRuleOperationType.SetPriority, Priority = RulePriority.Low };

        var result = BatchRuleOperations.Apply(Rule(priority: RulePriority.Medium), op);

        result.Should().NotBeNull();
        result!.Priority.Should().Be(RulePriority.Low);
    }

    [Fact]
    public void Apply_SetPriority_Same_ReturnsNull()
    {
        var op = new BatchRuleOperation { Type = BatchRuleOperationType.SetPriority, Priority = RulePriority.Medium };

        BatchRuleOperations.Apply(Rule(priority: RulePriority.Medium), op).Should().BeNull();
    }
}
