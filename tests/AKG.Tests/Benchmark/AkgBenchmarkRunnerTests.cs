using Edda.AKG.Benchmark;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Edda.AKG.Tests.Benchmark;

public class AkgBenchmarkRunnerTests
{
    private static KnowledgeRule Rule(string id) => new()
    {
        Id = id,
        Domain = "test",
        Type = "Rule",
        Priority = RulePriority.Medium,
        Body = id,
    };

    private static AkgBenchmarkRunner CreateRunner(IKnowledgeGraph graph)
        => new(graph, TimeProvider.System, new Mock<ILogger<AkgBenchmarkRunner>>().Object);

    private static Mock<IKnowledgeGraph> GraphReturning(
        Func<TaskContext, IReadOnlyList<KnowledgeRule>> active)
    {
        var graph = new Mock<IKnowledgeGraph>();
        graph.Setup(g => g.CompileContextAsync(It.IsAny<TaskContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskContext tc, CancellationToken _) =>
                new ContextResult { ActiveRules = active(tc), FormattedContext = "" });
        return graph;
    }

    [Fact]
    public async Task RunAsync_ScoresEachCaseAgainstCompiledRules()
    {
        var graph = GraphReturning(_ => [Rule("a"), Rule("b")]);
        var runner = CreateRunner(graph.Object);

        var dataset = new BenchmarkDataset
        {
            Name = "t",
            Cases = [new BenchmarkCase { Id = "c1", Query = "q1", ExpectedRuleIds = ["a"] }],
        };

        var report = await runner.RunAsync(dataset, k: 2);

        report.CaseCount.Should().Be(1);
        report.K.Should().Be(2);
        report.Cases[0].CaseId.Should().Be("c1");
        report.Cases[0].RetrievedRuleIds.Should().Equal("a", "b");
        report.Cases[0].Metrics.RecallAtK.Should().Be(1.0);
        report.Cases[0].Metrics.Mrr.Should().Be(1.0);
    }

    [Fact]
    public async Task RunAsync_AggregatesMeanAcrossCases()
    {
        // Case "hit" retrieves the expected rule (recall 1); case "miss" does not (recall 0). Mean = 0.5.
        var graph = new Mock<IKnowledgeGraph>();
        graph.Setup(g => g.CompileContextAsync(
                It.Is<TaskContext>(t => t.Task == "hit"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResult { ActiveRules = [Rule("a")], FormattedContext = "" });
        graph.Setup(g => g.CompileContextAsync(
                It.Is<TaskContext>(t => t.Task == "miss"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextResult { ActiveRules = [Rule("z")], FormattedContext = "" });
        var runner = CreateRunner(graph.Object);

        var dataset = new BenchmarkDataset
        {
            Name = "t",
            Cases =
            [
                new BenchmarkCase { Id = "c1", Query = "hit", ExpectedRuleIds = ["a"] },
                new BenchmarkCase { Id = "c2", Query = "miss", ExpectedRuleIds = ["a"] },
            ],
        };

        var report = await runner.RunAsync(dataset, k: 5);

        report.Aggregate.RecallAtK.Should().Be(0.5);
    }

    [Fact]
    public async Task RunAsync_EmptyExpected_YieldsZeroMetrics()
    {
        var graph = GraphReturning(_ => [Rule("a")]);
        var runner = CreateRunner(graph.Object);

        var dataset = new BenchmarkDataset
        {
            Name = "t",
            Cases = [new BenchmarkCase { Id = "c1", Query = "q", ExpectedRuleIds = [] }],
        };

        var report = await runner.RunAsync(dataset);

        report.Cases[0].Metrics.RecallAtK.Should().Be(0.0);
        report.Cases[0].RetrievedRuleIds.Should().Equal("a");
    }

    [Fact]
    public async Task RunAsync_PopulatesLatencyPercentiles()
    {
        var graph = GraphReturning(_ => [Rule("a")]);
        var runner = CreateRunner(graph.Object);

        var dataset = new BenchmarkDataset
        {
            Name = "t",
            Cases =
            [
                new BenchmarkCase { Id = "c1", Query = "q1", ExpectedRuleIds = ["a"] },
                new BenchmarkCase { Id = "c2", Query = "q2", ExpectedRuleIds = ["a"] },
            ],
        };

        var report = await runner.RunAsync(dataset);

        report.LatencyMsP50.Should().BeGreaterThanOrEqualTo(0.0);
        report.LatencyMsP95.Should().BeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public async Task RunAsync_EmptyDataset_ReturnsEmptyAggregate()
    {
        var graph = GraphReturning(_ => []);
        var runner = CreateRunner(graph.Object);

        var report = await runner.RunAsync(new BenchmarkDataset { Name = "empty" });

        report.CaseCount.Should().Be(0);
        report.Aggregate.RecallAtK.Should().Be(0.0);
        report.LatencyMsP50.Should().Be(0.0);
        report.Cases.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PassesQueryConceptsAndUserToContext()
    {
        TaskContext? captured = null;
        var graph = new Mock<IKnowledgeGraph>();
        graph.Setup(g => g.CompileContextAsync(It.IsAny<TaskContext>(), It.IsAny<CancellationToken>()))
            .Callback((TaskContext tc, CancellationToken _) => captured = tc)
            .ReturnsAsync(new ContextResult { ActiveRules = [Rule("a")], FormattedContext = "" });
        var runner = CreateRunner(graph.Object);

        var dataset = new BenchmarkDataset
        {
            Name = "t",
            Cases =
            [
                new BenchmarkCase
                {
                    Id = "c1",
                    Query = "the query",
                    ExpectedRuleIds = ["a"],
                    Concepts = ["x", "y"],
                    UserId = "user-7",
                },
            ],
        };

        await runner.RunAsync(dataset);

        captured.Should().NotBeNull();
        captured!.Task.Should().Be("the query");
        captured.Concepts.Should().Equal("x", "y");
        captured.UserId.Should().Be("user-7");
    }
}
