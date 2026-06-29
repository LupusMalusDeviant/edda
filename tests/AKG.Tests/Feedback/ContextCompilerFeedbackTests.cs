using Edda.AKG.Context;
using Edda.AKG.Tests.TestUtilities;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Neo4j.Driver;

namespace Edda.AKG.Tests.Feedback;

/// <summary>
/// Tests that <see cref="ContextCompiler"/> integrates correctly with
/// <see cref="IRuleFeedbackService"/> to apply confidence multipliers.
/// </summary>
public sealed class ContextCompilerFeedbackTests
{
    private readonly FakeCypherExecutor _cypher = new();
    private readonly Mock<IEmbeddingService> _embeddings = new();
    private readonly Mock<ILogger<ContextCompiler>> _logger = new();
    private readonly Mock<ILoggerFactory> _loggerFactory = new();
    private readonly Mock<IRuleFeedbackService> _feedbackService = new();

    public ContextCompilerFeedbackTests()
    {
        _embeddings.SetupGet(e => e.IsAvailable).Returns(false);
        _loggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<Microsoft.Extensions.Logging.ILogger>());
    }

    private ContextCompiler CreateCompiler(IRuleFeedbackService? feedbackService = null) => new(
        _cypher,
        _embeddings.Object,
        _logger.Object,
        _loggerFactory.Object,
        TimeProvider.System,
        feedbackService);

    private static INode MakeRuleNode(string id, string body = "test body")
    {
        var node = new Mock<INode>();
        node.SetupGet(n => n.Properties).Returns(new Dictionary<string, object?>
        {
            ["id"]       = id,
            ["body"]     = body,
            ["priority"] = "Medium",
            ["domain"]   = "general",
            ["type"]     = "Rule",
            ["tags"]     = new List<object>(),
        });
        return node.Object;
    }

    [Fact]
    public async Task CompileAsync_WithDegradedRule_ScoresLowerThanWithout()
    {
        // Two rules with identical keyword match — differentiated only by multiplier
        _cypher.DefaultResult =
        [
            new Dictionary<string, object?> { ["r"] = MakeRuleNode("rule-boosted", "important task keyword") },
            new Dictionary<string, object?> { ["r"] = MakeRuleNode("rule-degraded", "important task keyword") },
        ];

        _feedbackService
            .Setup(f => f.GetMultipliersAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, double>
            {
                ["rule-boosted"]  = 1.3,
                ["rule-degraded"] = 0.3,
            });

        _feedbackService
            .Setup(f => f.RecordUsageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ctx = new TaskContext { Task = "task keyword", UserId = "user-1" };
        var compiler = CreateCompiler(_feedbackService.Object);

        var result = await compiler.CompileAsync(ctx, CancellationToken.None);

        // rule-boosted should appear before rule-degraded
        var activeIds = result.ActiveRules.Select(r => r.Id).ToList();
        activeIds.Should().ContainInOrder("rule-boosted", "rule-degraded");
    }

    [Fact]
    public async Task CompileAsync_WithoutFeedbackService_StillCompiles()
    {
        _cypher.DefaultResult =
        [
            new Dictionary<string, object?> { ["r"] = MakeRuleNode("rule-1") },
        ];

        var ctx = new TaskContext { Task = "test", UserId = "user-1" };
        var compiler = CreateCompiler(feedbackService: null);

        var act = async () => await compiler.CompileAsync(ctx, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CompileAsync_RecordsUsageForEachScoredRule()
    {
        _cypher.DefaultResult =
        [
            new Dictionary<string, object?> { ["r"] = MakeRuleNode("rule-X") },
        ];

        _feedbackService
            .Setup(f => f.GetMultipliersAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, double>());

        _feedbackService
            .Setup(f => f.RecordUsageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var ctx = new TaskContext { Task = "test", UserId = "user-1" };
        var compiler = CreateCompiler(_feedbackService.Object);

        await compiler.CompileAsync(ctx, CancellationToken.None);

        _feedbackService.Verify(
            f => f.RecordUsageAsync("rule-X", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
