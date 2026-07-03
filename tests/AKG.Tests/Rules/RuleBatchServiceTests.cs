using Edda.AKG.Rules;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Tests.Rules;

/// <summary>Unit tests for <see cref="RuleBatchService"/> (E8): mutation, ownership, and per-batch audit.</summary>
public sealed class RuleBatchServiceTests
{
    private readonly Mock<IKnowledgeGraph> _graph = new();
    private readonly Mock<IAuditLog> _audit = new();
    private readonly RuleBatchService _sut;

    public RuleBatchServiceTests()
    {
        _audit.Setup(a => a.LogAsync(
                It.IsAny<AuditEvent>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _graph.Setup(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgeRule r, CancellationToken _) => r);
        _sut = new RuleBatchService(_graph.Object, _audit.Object, NullLogger<RuleBatchService>.Instance);
    }

    private static KnowledgeRule Rule(string id, IReadOnlyList<string>? tags = null, string owner = "user-1")
        => new() { Id = id, Type = "Rule", Domain = "d", Priority = RulePriority.Medium, Body = "b", OwnerId = owner, Tags = tags ?? [] };

    private void SetupRule(string id, KnowledgeRule? rule)
        => _graph.Setup(g => g.GetRuleAsync(id, "user-1", It.IsAny<CancellationToken>())).ReturnsAsync(rule);

    private static BatchRuleOperation AddTag(string tag) => new() { Type = BatchRuleOperationType.AddTag, Tag = tag };

    [Fact]
    public async Task ApplyAsync_AddTag_UpsertsModifiedRules_AndCountsUpdated()
    {
        SetupRule("a", Rule("a"));
        SetupRule("b", Rule("b"));

        var result = await _sut.ApplyAsync(AddTag("x"), ["a", "b"], "user-1", isAdmin: true);

        result.Updated.Should().Be(2);
        _graph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ApplyAsync_RuleNotFound_Skips()
    {
        SetupRule("a", null);

        var result = await _sut.ApplyAsync(AddTag("x"), ["a"], "user-1", isAdmin: true);

        result.Skipped.Should().Be(1);
        _graph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_NoOp_Skips()
    {
        SetupRule("a", Rule("a", tags: ["x"]));   // already carries "x"

        var result = await _sut.ApplyAsync(AddTag("x"), ["a"], "user-1", isAdmin: true);

        result.Skipped.Should().Be(1);
        _graph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_NonAdmin_ForeignRule_Skipped()
    {
        SetupRule("a", Rule("a", owner: "someone-else"));

        var result = await _sut.ApplyAsync(AddTag("x"), ["a"], "user-1", isAdmin: false);

        result.Skipped.Should().Be(1);
        _graph.Verify(g => g.UpsertRuleAsync(It.IsAny<KnowledgeRule>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_WritesExactlyOneAuditEntry()
    {
        SetupRule("a", Rule("a"));

        await _sut.ApplyAsync(AddTag("x"), ["a"], "user-1", isAdmin: true);

        _audit.Verify(a => a.LogAsync(
            AuditEvent.RuleBatchUpdated, "user-1", It.IsAny<string>(),
            It.IsAny<IDictionary<string, object?>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_UpsertThrows_CountsFailed_AndContinues()
    {
        SetupRule("a", Rule("a"));
        SetupRule("b", Rule("b"));
        _graph.Setup(g => g.UpsertRuleAsync(It.Is<KnowledgeRule>(r => r.Id == "a"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _sut.ApplyAsync(AddTag("x"), ["a", "b"], "user-1", isAdmin: true);

        result.Failed.Should().Be(1);
        result.Updated.Should().Be(1);   // b still processed after a failed
    }
}
