using System.Text.Json;
using Edda.Core.Models;

namespace Edda.Core.Tests.Models;

/// <summary>Unit tests for <see cref="KnowledgeBundle"/> serialization round-tripping.</summary>
public sealed class KnowledgeBundleTests
{
    [Fact]
    public void RoundTrip_PreservesRulesRelationsAndPriority()
    {
        var bundle = new KnowledgeBundle
        {
            Rules =
            [
                new KnowledgeRule
                {
                    Id = "r1",
                    Type = "Guideline",
                    Domain = "ops",
                    Priority = RulePriority.High,
                    Body = "Body",
                    Tags = ["t"],
                    RelatesTo = new RuleRelations { Related = ["r2"] },
                },
            ],
        };

        var json = JsonSerializer.Serialize(bundle, KnowledgeBundleSerialization.Options);
        var restored = JsonSerializer.Deserialize<KnowledgeBundle>(json, KnowledgeBundleSerialization.Options);

        restored.Should().NotBeNull();
        restored!.Rules.Should().ContainSingle();
        restored.Rules[0].Id.Should().Be("r1");
        restored.Rules[0].Priority.Should().Be(RulePriority.High);
        restored.Rules[0].RelatesTo!.Related.Should().Contain("r2");
    }

    [Fact]
    public void Serialize_WritesPriorityAsReadableString()
    {
        var bundle = new KnowledgeBundle
        {
            Rules = [new KnowledgeRule { Id = "r", Type = "Rule", Domain = "d", Priority = RulePriority.Critical, Body = "b" }],
        };

        var json = JsonSerializer.Serialize(bundle, KnowledgeBundleSerialization.Options);

        json.Should().Contain("Critical");
    }
}
