using Edda.AKG.Ingestion.Entities;
using Edda.AKG.Ingestion.Evaluation;
using Edda.AKG.Ingestion.Llm;
using Edda.Core.Abstractions;
using Edda.Core.Models;
using Edda.Security.OutputFilter;
using Edda.Security.Sanitization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Edda.AKG.Ingestion.Tests.Evaluation;

/// <summary>Unit tests for <see cref="ExtractionEvaluator"/> (mocked extractor and real extractor + mocked LLM).</summary>
public class ExtractionEvaluatorTests
{
    private readonly ExtractionEvaluator _sut = new();

    private static IEntityExtractor Extractor(EntityExtractionResult result)
    {
        var mock = new Mock<IEntityExtractor>();
        mock.Setup(e => e.ExtractAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static ExtractionEvalDataset OneCase(EntityExtractionResult expected)
        => new()
        {
            Name = "t",
            Cases = [new ExtractionEvalCase { Id = "c1", Text = "some text", Expected = expected }],
        };

    [Fact]
    public async Task EvaluateAsync_ExactMatch_ScoresOne()
    {
        var golden = new EntityExtractionResult
        {
            Entities = [new ExtractedEntity { Name = "Neo4j" }, new ExtractedEntity { Name = "Cypher" }],
            Relations = [new ExtractedRelation { Source = "Neo4j", Target = "Cypher" }],
        };

        var report = await _sut.EvaluateAsync(Extractor(golden), OneCase(golden));

        report.EntityScore.F1.Should().Be(1.0);
        report.RelationScore.F1.Should().Be(1.0);
        report.CaseCount.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_MatchesCaseInsensitivelyAndTrimmed()
    {
        var golden = new EntityExtractionResult { Entities = [new ExtractedEntity { Name = "Neo4j" }] };
        var actual = new EntityExtractionResult { Entities = [new ExtractedEntity { Name = "  neo4j " }] };

        var report = await _sut.EvaluateAsync(Extractor(actual), OneCase(golden));

        report.EntityScore.Recall.Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_MissedEntity_LowersRecallKeepsPrecision()
    {
        var golden = new EntityExtractionResult
        {
            Entities = [new ExtractedEntity { Name = "A" }, new ExtractedEntity { Name = "B" }],
        };
        var actual = new EntityExtractionResult { Entities = [new ExtractedEntity { Name = "A" }] };

        var report = await _sut.EvaluateAsync(Extractor(actual), OneCase(golden));

        report.EntityScore.Precision.Should().Be(1.0);
        report.EntityScore.Recall.Should().Be(0.5);
    }

    [Fact]
    public async Task EvaluateAsync_RealExtractor_WithMockedChat_ScoresParsedOutput()
    {
        // The real LlmEntityExtractor plus a mocked ILlmChatClient returning canned JSON — proves the harness
        // runs end-to-end through the actual extraction/parse path without a real LLM.
        var chat = new Mock<ILlmChatClient>();
        chat.SetupGet(c => c.ProviderName).Returns("mock");
        chat.Setup(c => c.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                "{\"entities\":[{\"name\":\"Neo4j\",\"type\":\"technology\"}," +
                "{\"name\":\"Cypher\",\"type\":\"technology\"}]," +
                "\"relations\":[{\"source\":\"Neo4j\",\"target\":\"Cypher\",\"description\":\"queried with\"}]}");

        var extractor = new LlmEntityExtractor(
            chat.Object, new InputSanitizer(), new SecretRedactor(), NullLogger<LlmEntityExtractor>.Instance);

        var golden = new EntityExtractionResult
        {
            Entities = [new ExtractedEntity { Name = "Neo4j" }, new ExtractedEntity { Name = "Cypher" }],
            Relations = [new ExtractedRelation { Source = "Neo4j", Target = "Cypher" }],
        };
        var dataset = new ExtractionEvalDataset
        {
            Name = "e2e",
            Cases = [new ExtractionEvalCase { Id = "c1", Text = "Neo4j is queried with Cypher.", Expected = golden }],
        };

        var report = await _sut.EvaluateAsync(extractor, dataset);

        report.EntityScore.F1.Should().Be(1.0);
        report.RelationScore.F1.Should().Be(1.0);
    }
}
