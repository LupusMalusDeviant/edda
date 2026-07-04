using Edda.AKG.Background;
using Edda.AKG.Benchmark;
using Edda.AKG.Chunking;
using Edda.AKG.Context;
using Edda.AKG.Embeddings;
using Edda.AKG.Graph;
using Edda.Core.Abstractions;
using Edda.Core.Benchmark;
using Edda.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit.Abstractions;

namespace Edda.AKG.Tests.Benchmark;

/// <summary>
/// Reproducible scale/latency benchmark of <see cref="Neo4jKnowledgeGraph.CompileContextAsync"/> over the real
/// in-memory dev executor (no Neo4j, no embeddings). The corpus size is deterministic and configurable via the
/// <c>EDDA_BENCH_RULES</c>/<c>EDDA_BENCH_CASES</c>/<c>EDDA_BENCH_SEED</c> environment variables; the default is
/// small so the suite stays fast, while a scale run (e.g. <c>EDDA_BENCH_RULES=100000</c>) reuses the same code
/// path. Measures the keyword + graph pipeline — the path that scales with corpus size; the semantic phase is
/// off because no embedding provider is configured.
/// </summary>
public sealed class ScaleBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public ScaleBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Benchmark_Runs_AtConfiguredScale()
    {
        var ruleCount = EnvInt("EDDA_BENCH_RULES", 300);
        var caseCount = EnvInt("EDDA_BENCH_CASES", 20);
        var seed = EnvInt("EDDA_BENCH_SEED", 1);

        var report = await RunBenchmarkAsync(ruleCount, caseCount, seed);

        _output.WriteLine($"Scale benchmark (in-memory, keyword+graph): rules={ruleCount}, cases={report.CaseCount}, seed={seed}");
        _output.WriteLine(
            $"  recall@{report.K}={report.Aggregate.RecallAtK:F3}  mrr={report.Aggregate.Mrr:F3}  ndcg@{report.K}={report.Aggregate.NdcgAtK:F3}");
        _output.WriteLine(
            $"  latency P50={report.LatencyMsP50:F1}ms  P95={report.LatencyMsP95:F1}ms  avgTokens={report.AvgEstimatedTokens:F0}");

        report.CaseCount.Should().BeGreaterThan(0);
        report.LatencyMsP50.Should().BeGreaterThanOrEqualTo(0);
    }

    private static int EnvInt(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

    private static async Task<BenchmarkReport> RunBenchmarkAsync(int ruleCount, int caseCount, int seed)
    {
        var executor = new InMemoryCypherExecutor();
        var graph = BuildGraph(executor);

        var corpus = new SyntheticBenchmarkGenerator().Generate(ruleCount, caseCount, seed);

        // Bulk mode suppresses per-upsert inline embedding (none is configured anyway).
        using (graph.BeginBulkIngestion())
        {
            foreach (var rule in corpus.Rules)
                await graph.UpsertRuleAsync(rule);
        }

        var runner = new AkgBenchmarkRunner(graph, TimeProvider.System, Mock.Of<ILogger<AkgBenchmarkRunner>>());
        return await runner.RunAsync(corpus.Dataset);
    }

    private static Neo4jKnowledgeGraph BuildGraph(ICypherExecutor executor)
    {
        var embeddings = new Mock<IEmbeddingService>();
        embeddings.SetupGet(e => e.IsAvailable).Returns(false);

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<Microsoft.Extensions.Logging.ILogger>());

        var compiler = new ContextCompiler(
            executor, embeddings.Object, Mock.Of<ILogger<ContextCompiler>>(), loggerFactory.Object, TimeProvider.System);

        var fs = new Mock<IFileSystem>();
        fs.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(false);
        fs.Setup(f => f.GetFullPath(It.IsAny<string>())).Returns("/knowledge");

        var headVectorStore = new Mock<IHeadVectorStore>();
        headVectorStore.Setup(s => s.GetCoverageAsync(It.IsAny<CancellationToken>())).ReturnsAsync((0, 0));

        return new Neo4jKnowledgeGraph(
            executor,
            compiler,
            new RuleLoader(fs.Object, executor, TimeProvider.System, Mock.Of<ILogger<RuleLoader>>()),
            new WorldKnowledgeSeeder(fs.Object, executor, Mock.Of<ILogger<WorldKnowledgeSeeder>>()),
            new Neo4jEmbeddingCache(
                executor, embeddings.Object, new AdaptiveDocumentChunker(), () => new ChunkingOptions(),
                Mock.Of<ILogger<Neo4jEmbeddingCache>>()),
            headVectorStore.Object,
            fs.Object,
            TimeProvider.System,
            new ChannelBackgroundWorkQueue(),
            Mock.Of<ILogger<Neo4jKnowledgeGraph>>());
    }
}
